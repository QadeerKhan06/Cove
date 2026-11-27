using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Cove.UI.Pill;

/// <summary>
/// The pill: Cove's one window, a capsule centred at the top of the main monitor. It stays on top,
/// has no taskbar button, isn't in Alt+Tab, and never takes focus from the app you're using.
/// </summary>
/// <remarks>
/// <para>
/// The code-behind only sets window styles and placement, which are purely visual (CLAUDE.md 10.4).
/// </para>
/// <para>
/// The window itself must stay fully see-through around the capsule, so WPF-UI's window styling
/// (SystemThemeWatcher, Mica, MainWindow handling) must never be applied to it: that paints the
/// window's background. Its colours come from the app's theme resources, so it follows light
/// and dark mode without any of that.
/// </para>
/// </remarks>
public sealed partial class PillWindow : Window
{
    // CLAUDE.md 9.5: 8 px below the top edge, in DPI-independent pixels.
    private const double TopGap = 8;

    // Windows counts 96 DPI as 100% scaling.
    private const double DpiAt100Percent = 96;

    private HWND _handle;

    public PillWindow()
    {
        InitializeComponent();

        // Create the native window now, while hidden, so its styles are set before it's ever shown.
        new WindowInteropHelper(this).EnsureHandle();

        SizeChanged += (_, _) => PlaceAtTopCenter();
        DpiChanged += (_, _) => PlaceAtTopCenter();
        IsVisibleChanged += (_, _) => PlaceAtTopCenter();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _handle = (HWND)handle;
        AddExtendedStyles(_handle);
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
    }

    // See "Extended Window Styles" in Microsoft's Win32 docs. GetWindowLong rather than
    // GetWindowLongPtr: extended styles are 32-bit flags, so it's correct on 64-bit Windows too.
    private static void AddExtendedStyles(HWND handle)
    {
        var styles = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        // WS_EX_NOACTIVATE: showing or clicking the pill never takes focus from the app you're in.
        // WS_EX_TOOLWINDOW: no taskbar button, and left out of Alt+Tab.
        styles |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;

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
        if (_handle.IsNull || ActualWidth <= 0)
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

        var bounds = info.rcMonitor;
        var (x, y) = PillPlacement.TopCenter(
            bounds.left, bounds.top, bounds.right - bounds.left, GetMonitorScale(monitor), ActualWidth, TopGap);

        // NOSIZE: WPF sizes the window to its content. NOZORDER: it stays topmost. NOACTIVATE: no focus.
        PInvoke.SetWindowPos(
            _handle,
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
