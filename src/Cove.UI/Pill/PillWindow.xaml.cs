using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cove.Core.Contracts;
using Cove.UI.Kit;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Cove.UI.Pill;

/// <summary>
/// The pill: Cove's one window, centred at the top of the main monitor. It stays on top, has no
/// taskbar button, isn't in Alt+Tab, and takes focus only in states that need it (a panel).
/// <see cref="PillController"/> decides what it shows, through <see cref="IPillView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The code-behind only handles window styles, placement, animation and focus, which are purely
/// visual (CLAUDE.md 10.4).
/// </para>
/// <para>
/// The window is a fixed, see-through area that never moves or resizes; only the surface inside
/// it slides, fades and changes size. So nothing jumps sideways while the pill grows, and clicks
/// outside the surface reach the app underneath.
/// </para>
/// <para>
/// WPF-UI's window styling (SystemThemeWatcher, Mica, MainWindow handling) must never be applied to
/// this window: it paints the window's background, which would block those clicks. Its colours come
/// from the app's theme resources, so it follows light and dark mode without any of that.
/// </para>
/// </remarks>
public sealed partial class PillWindow : Window, IPillView
{
    // Windows counts 96 DPI as 100% scaling.
    private const double DpiAt100Percent = 96;

    // Motion from the design (SPEC.md §14, CLAUDE.md 9.5): appearing and growing take 250 ms,
    // hiding and shrinking 167 ms, and the pill slides 8 px at the top edge. C-15 adds "width
    // first, then height", the content fade, and reduced motion.
    private const double SlideDistance = 8;
    private static readonly Duration GrowDuration = TimeSpan.FromMilliseconds(250);
    private static readonly Duration ShrinkDuration = TimeSpan.FromMilliseconds(167);

    private readonly Style _alertSurfaceStyle;
    private readonly Style _panelSurfaceStyle;
    private readonly Style _alertTextStyle;
    private readonly Style _iconStyle;
    private readonly Style _dotStyle;
    private readonly Style _actionButtonStyle;
    private readonly Thickness _contentPadding;
    private readonly Thickness _contentPaddingWithAction;

    private nint _handle;
    private PillState _shownState = PillState.Hidden;

    // Counts state changes, so a slide-out that ends after the pill was shown again doesn't hide it.
    private int _transition;

    public PillWindow()
    {
        InitializeComponent();
        _alertSurfaceStyle = (Style)FindResource("PillAlertSurfaceStyle");
        _panelSurfaceStyle = (Style)FindResource("PillPanelSurfaceStyle");
        _alertTextStyle = (Style)FindResource("PillAlertTextStyle");
        _iconStyle = (Style)FindResource("PillIconStyle");
        _dotStyle = (Style)FindResource("PillDotStyle");
        _actionButtonStyle = (Style)FindResource("PillActionButtonStyle");
        _contentPadding = (Thickness)FindResource("PillContentPadding");
        _contentPaddingWithAction = (Thickness)FindResource("PillContentPaddingWithAction");

        // Starts slid up and see-through, ready to slide in the first time it's shown.
        Slide.Y = -SlideDistance;
        Surface.Opacity = 0;

        // Create the native window now, while hidden, so its styles are set before it's ever shown.
        new WindowInteropHelper(this).EnsureHandle();

        SizeChanged += (_, _) => PlaceAtTopCenter();
        DpiChanged += (_, _) => PlaceAtTopCenter();
        IsVisibleChanged += (_, _) => PlaceAtTopCenter();
    }

    /// <inheritdoc />
    public event EventHandler? EscapePressed;

    /// <inheritdoc />
    public event EventHandler? ClickedOutside;

    /// <inheritdoc />
    public event EventHandler<CoveAction>? AlertActionClicked;

    /// <inheritdoc />
    public nint GetForegroundWindow() => (nint)PInvoke.GetForegroundWindow();

    /// <inheritdoc />
    public void ShowAlert(CoveEvent alert)
    {
        ShowState(PillState.Alert, BuildAlert(alert), _alertSurfaceStyle);
        AutomationProperties.SetName(this, "Cove");
    }

    /// <inheritdoc />
    public bool ShowPanel(string title, object view)
    {
        // IPanel's contract: CreateView returns a WPF element built from the UI kit.
        if (view is not UIElement content)
        {
            return false;
        }

        try
        {
            ShowState(PillState.Panel, content, _panelSurfaceStyle);
        }
        catch (InvalidOperationException ex)
        {
            // For example, the view is still inside another window.
            Log.Warning("Couldn't put a panel's view in the pill ({Error})", ex.GetType().Name);
            return false;
        }

        // Screen readers announce the panel's title when focus moves into it.
        AutomationProperties.SetName(this, title);
        return true;
    }

    // Explicit, because Window.Hide() would hide at once, without the slide.
    void IPillView.Hide()
    {
        if (_shownState == PillState.Hidden)
        {
            return;
        }

        _shownState = PillState.Hidden;
        var transition = ++_transition;
        SetActivatable(false);

        var slide = AnimateTo(-SlideDistance, ShrinkDuration, Accelerate());
        slide.Completed += (_, _) =>
        {
            // Shown again while sliding out: leave it on screen.
            if (transition != _transition)
            {
                return;
            }

            base.Hide();
            StopResize();
            Surface.Child = null; // lets a closed panel's view go
        };
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);
        Surface.BeginAnimation(OpacityProperty, AnimateTo(0, ShrinkDuration, Accelerate()));
    }

    /// <inheritdoc />
    public void ReturnFocus(nint window)
    {
        // That window may have closed meanwhile, and focus never "returns" to the pill itself.
        if (window == 0 || window == _handle || !PInvoke.IsWindow((HWND)window))
        {
            return;
        }

        if (!PInvoke.SetForegroundWindow((HWND)window))
        {
            Log.Warning("Couldn't give focus back to the window that had it");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;
        SetActivatable(false);
        HwndSource.FromHwnd(_handle)?.AddHook(OnWindowMessage);
    }

    // Bubbling KeyDown, not PreviewKeyDown: the panel sees Esc first and marks it handled when it
    // steps back a level (CLAUDE.md 9.5). Only an Esc the panel didn't use reaches the pill.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled && _shownState.TakesFocus())
        {
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Another window took focus (a click elsewhere, Alt+Tab); the controller decides whether the
    // panel closes. While hidden or sliding out, losing focus means nothing.
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_shownState.TakesFocus())
        {
            ClickedOutside?.Invoke(this, EventArgs.Empty);
        }
    }

    // Close to Fluent's "decelerate" and "accelerate" curves (SPEC.md §14).
    private static QuinticEase Decelerate() => new() { EasingMode = EasingMode.EaseOut };

    private static QuinticEase Accelerate() => new() { EasingMode = EasingMode.EaseIn };

    // Starts from wherever the value is now, so an interrupted slide carries on smoothly.
    private static DoubleAnimation AnimateTo(double to, Duration duration, IEasingFunction easing) =>
        new(to, duration) { EasingFunction = easing };

    // The design's alert (SPEC.md §7): icon · one line · at most one button.
    private DockPanel BuildAlert(CoveEvent alert)
    {
        // The design allows one action per alert; the queue logs when an event carries more.
        var action = alert.Actions.Count > 0 ? alert.Actions[0] : null;
        var line = new DockPanel { Margin = action is null ? _contentPadding : _contentPaddingWithAction };

        var icon = CreateIcon(AlertIcons.For(alert.Type));
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);

        if (action is not null)
        {
            var button = new Button { Content = action.Label, Style = _actionButtonStyle };
            button.Click += (_, _) => AlertActionClicked?.Invoke(this, action);
            DockPanel.SetDock(button, Dock.Right);
            line.Children.Add(button);
        }

        // Last, so it gets the space left between the icon and the button.
        line.Children.Add(new TextBlock { Text = alert.Message, Style = _alertTextStyle });
        return line;
    }

    private FrameworkElement CreateIcon(AlertIcon icon)
    {
        if (icon.Glyph is null)
        {
            var dot = new Ellipse { Style = _dotStyle };
            dot.SetResourceReference(Shape.FillProperty, icon.BrushKey);
            return dot;
        }

        // Decorative: screen readers skip it, since the alert's line already says what happened.
        // The colour is a theme reference, so it follows a light/dark switch while the alert is up.
        var glyph = new DecorativeGlyph { Text = icon.Glyph, Style = _iconStyle };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, icon.BrushKey);
        return glyph;
    }

    // Shows the surface in a new state: slides in if the pill was hidden, otherwise changes size.
    private void ShowState(PillState state, UIElement content, Style surfaceStyle)
    {
        var from = new Size(Surface.ActualWidth, Surface.ActualHeight);
        var wasShown = _shownState != PillState.Hidden;

        Surface.Child = content;
        Surface.Style = surfaceStyle;
        _shownState = state;
        _transition++;

        // Focus belongs to the state (CLAUDE.md 9.5): only a state that takes it may be activated.
        SetActivatable(state.TakesFocus());
        if (!IsVisible)
        {
            Show();
        }

        if (wasShown)
        {
            AnimateResize(from);
        }
        else
        {
            SlideIn();
        }

        if (state.TakesFocus())
        {
            TakeFocus(content);
        }
    }

    private void SlideIn()
    {
        // The surface takes its new size at once; only its position and opacity animate.
        StopResize();
        Slide.BeginAnimation(TranslateTransform.YProperty, AnimateTo(0, GrowDuration, Decelerate()));
        Surface.BeginAnimation(OpacityProperty, AnimateTo(1, GrowDuration, Decelerate()));
    }

    private void AnimateResize(Size from)
    {
        // Measure the size the new content wants, with no earlier resize still holding the surface.
        // Its style's limits (e.g. a panel's 400 x 420) apply even with unlimited room.
        StopResize();
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var to = Surface.DesiredSize;

        var grows = to.Width * to.Height >= from.Width * from.Height;
        var duration = grows ? GrowDuration : ShrinkDuration;

        // FillBehavior.Stop: afterwards the surface sizes itself to its content again, so a panel
        // that grows later (files added) still fits.
        Surface.BeginAnimation(WidthProperty, Resize(from.Width, to.Width));
        Surface.BeginAnimation(HeightProperty, Resize(from.Height, to.Height));

        DoubleAnimation Resize(double start, double end) => new(start, end, duration)
        {
            EasingFunction = grows ? Decelerate() : Accelerate(),
            FillBehavior = FillBehavior.Stop,
        };
    }

    private void StopResize()
    {
        Surface.BeginAnimation(WidthProperty, null);
        Surface.BeginAnimation(HeightProperty, null);
    }

    private void TakeFocus(UIElement content)
    {
        // Windows only lets an app take focus while it's handling the user's input, such as a
        // shortcut press (see SetForegroundWindow in Microsoft's docs); otherwise Activate fails.
        if (!Activate())
        {
            Log.Warning("Windows didn't let the pill take focus, so the panel is shown without it");
        }

        // Once it's laid out, focus the panel's first control, unless the panel already chose one.
        Dispatcher.InvokeAsync(
            () =>
            {
                if (!content.IsKeyboardFocusWithin)
                {
                    content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
            },
            DispatcherPriority.Loaded);
    }

    // See "Extended Window Styles" in Microsoft's Win32 docs. GetWindowLong rather than
    // GetWindowLongPtr: extended styles are 32-bit flags, so it's correct on 64-bit Windows too.
    private void SetActivatable(bool activatable)
    {
        var handle = (HWND)_handle;
        var styles = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        // WS_EX_TOOLWINDOW: no taskbar button, and left out of Alt+Tab.
        styles |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;

        // WS_EX_NOACTIVATE: showing or clicking the pill never takes focus from the app you're in.
        // Only a state that takes focus (a panel) switches it off.
        styles = activatable
            ? styles & ~WINDOW_EX_STYLE.WS_EX_NOACTIVATE
            : styles | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

        // A WPF window always has some extended styles already, so 0 here means it failed.
        if (PInvoke.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)styles) == 0)
        {
            Log.Warning("Couldn't set the pill's window styles (Win32 error {Error})", Marshal.GetLastWin32Error());
        }
    }

    // Puts the pill at the top centre of the main monitor, in physical pixels and using that
    // monitor's own scaling, so it's in the same place at every setting from 100% to 200%.
    private void PlaceAtTopCenter()
    {
        if (_handle == 0 || ActualWidth <= 0)
        {
            return;
        }

        // (0, 0) is always on the main monitor, so this finds it.
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            Log.Warning("Couldn't read the main monitor's size (Win32 error {Error})", Marshal.GetLastWin32Error());
            return;
        }

        // The 8 px gap is inside the window (PillTopGap), so the window itself sits at the very top.
        var bounds = info.rcMonitor;
        var (x, y) = PillPlacement.TopCenter(
            bounds.left, bounds.top, bounds.right - bounds.left, GetMonitorScale(monitor), ActualWidth, topGap: 0);

        // NOSIZE: the window keeps its size. NOZORDER: it stays topmost. NOACTIVATE: no focus.
        PInvoke.SetWindowPos(
            (HWND)_handle,
            HWND.Null,
            x,
            y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    // The main monitor's scaling, not this window's: before its first move the window may still
    // be on another monitor with a different scale.
    private double GetMonitorScale(HMONITOR monitor)
    {
        var result = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        return result.Succeeded ? dpiX / DpiAt100Percent : VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    // Screen resolution or layout changed: re-centre once WPF has caught up.
    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == (int)PInvoke.WM_DISPLAYCHANGE)
        {
            Dispatcher.InvokeAsync(PlaceAtTopCenter);
        }

        return IntPtr.Zero;
    }
}
