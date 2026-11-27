using Cove.Core.Contracts;
using Cove.Core.Events;
using Serilog;

namespace Cove.UI.Pill;

/// <summary>
/// Decides what the pill shows (Hidden, Alert or Panel) and where keyboard focus goes. Modules open
/// panels through <see cref="IPanelHost"/>; the <see cref="AlertQueue"/> shows alerts through
/// <see cref="IAlertDisplay"/>.
/// </summary>
/// <remarks>
/// <para>
/// Focus belongs to each state (<see cref="PillStateExtensions.TakesFocus"/>). Entering a state that
/// takes it remembers the window that had it, so Esc or <see cref="ClosePanel"/> can give it back
/// (the cursor returns to the same spot in Word). Moving between two such states keeps that window.
/// </para>
/// <para>
/// A click on another window closes the panel without moving focus: the user just chose where it
/// goes. A panel that waits for input (<see cref="IPanel.ClosesOnOutsideClick"/> false) stays open
/// instead, and if it's closed later, focus goes to the app the user was in last.
/// </para>
/// <para>
/// Panels are module code, so anything they throw is logged and the panel is closed; Cove keeps
/// running (CLAUDE.md 10.3). Use this class on the UI thread only, like the window it drives.
/// </para>
/// </remarks>
public sealed class PillController : IPanelHost, IAlertDisplay
{
    private readonly IPillView _view;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, IPanel> _panels = new(StringComparer.Ordinal);

    private IPanel? _openPanel;

    // Where focus goes when the pill leaves a state that takes it; 0 while it's in one that doesn't.
    private nint _focusToReturn;

    /// <param name="view">The pill's window.</param>
    /// <param name="panels">Every panel modules offer. A duplicate id or menu letter is logged and skipped.</param>
    /// <param name="logger">Where panel failures are logged. Leave it out to use Cove's log; tests pass their own.</param>
    public PillController(IPillView view, IEnumerable<IPanel> panels, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(panels);
        _view = view;
        _logger = logger;
        Register(panels);

        // Esc only reaches the pill when the panel didn't use it to step back.
        _view.EscapePressed += (_, _) => ClosePanel();
        _view.ClickedOutside += (_, _) => OnClickedOutside();
        _view.AlertActionClicked += OnAlertActionClicked;
    }

    /// <summary>Raised after a panel closes, so alerts held while it was open can show (C-07).</summary>
    public event EventHandler? PanelClosed;

    /// <inheritdoc />
    public event EventHandler<CoveAction>? ActionClicked;

    /// <summary>What the pill is showing now.</summary>
    public PillState State { get; private set; } = PillState.Hidden;

    // Looked up each time rather than stored, so a controller created before logging starts still logs.
    private ILogger Logger => _logger ?? Log.Logger;

    /// <inheritdoc />
    public void OpenPanel(string panelId, object? parameter = null)
    {
        if (!_panels.TryGetValue(panelId, out var panel))
        {
            Logger.Warning("No panel has the id {PanelId}, so nothing opened", panelId);
            return;
        }

        if (!State.TakesFocus())
        {
            // Read before the panel takes focus; afterwards the pill itself is in front (CLAUDE.md 11.1).
            _focusToReturn = _view.GetForegroundWindow();
        }

        if (_openPanel is { } current)
        {
            // Swapping panels: focus still goes back to the original app when the last one closes.
            _openPanel = null;
            NotifyClosed(current);
        }

        if (!TryShow(panel))
        {
            if (State == PillState.Panel)
            {
                // The panel that was open before is still on screen, with nothing behind it.
                EndPanel(returnFocus: true);
            }

            _focusToReturn = 0;
            return;
        }

        _openPanel = panel;
        State = PillState.Panel;

        try
        {
            panel.OnOpened(parameter);
        }
        catch (Exception ex)
        {
            LogPanelFailure(panel, nameof(IPanel.OnOpened), ex);
            ClosePanel();
        }
    }

    /// <inheritdoc />
    public void ClosePanel() => Close(returnFocus: true);

    /// <summary>Shows an alert without taking focus, in place of any alert already showing.</summary>
    /// <returns>
    /// False while a panel is open: the alert isn't shown, and the alert queue holds it until
    /// <see cref="PanelClosed"/>.
    /// </returns>
    public bool ShowAlert(CoveEvent alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (State == PillState.Panel)
        {
            return false;
        }

        State = PillState.Alert;
        _view.ShowAlert(alert);
        return true;
    }

    /// <summary>Hides the alert, if one is showing.</summary>
    public void HideAlert()
    {
        if (State != PillState.Alert)
        {
            return;
        }

        State = PillState.Hidden;
        _view.Hide();
    }

    // IPanel's contract: a duplicate id or menu letter would make OpenPanel or the Menu ambiguous,
    // so the second panel is skipped.
    private void Register(IEnumerable<IPanel> panels)
    {
        var lettersInUse = new HashSet<char>();
        foreach (var panel in panels)
        {
            if (_panels.ContainsKey(panel.Id))
            {
                Logger.Error("Two panels have the id {PanelId}; only the first is used", panel.Id);
                continue;
            }

            // The Menu treats 'd' and 'D' as the same key.
            if (!lettersInUse.Add(char.ToUpperInvariant(panel.MenuLetter)))
            {
                Logger.Error(
                    "Panel {PanelId} uses menu letter {Letter}, which another panel already has; it was skipped",
                    panel.Id,
                    panel.MenuLetter);
                continue;
            }

            _panels.Add(panel.Id, panel);
        }
    }

    private bool TryShow(IPanel panel)
    {
        object? view;
        try
        {
            view = panel.CreateView();
        }
        catch (Exception ex)
        {
            LogPanelFailure(panel, nameof(IPanel.CreateView), ex);
            return false;
        }

        if (view is not null && _view.ShowPanel(panel.Title, view))
        {
            return true;
        }

        // IPanel's contract: CreateView must return a WPF element built from the UI kit.
        Logger.Error(
            "Panel {PanelId} returned {ViewType}, which the pill can't show",
            panel.Id,
            view?.GetType().Name ?? "null");
        return false;
    }

    // Passed on to the alert queue, which runs the action. A click that lands as the alert is
    // replaced by a panel is ignored.
    private void OnAlertActionClicked(object? sender, CoveAction action)
    {
        if (State == PillState.Alert)
        {
            ActionClicked?.Invoke(this, action);
        }
    }

    private void OnClickedOutside()
    {
        if (_openPanel is not { } panel)
        {
            return;
        }

        if (ShouldCloseOnOutsideClick(panel))
        {
            // The window the user clicked already has focus.
            Close(returnFocus: false);
            return;
        }

        // The panel stays on screen without focus, e.g. while files are dragged in from Explorer.
        // If it's closed later, focus goes to the app the user was in last.
        var clickedWindow = _view.GetForegroundWindow();
        if (clickedWindow != 0)
        {
            _focusToReturn = clickedWindow;
        }
    }

    private bool ShouldCloseOnOutsideClick(IPanel panel)
    {
        try
        {
            return panel.ClosesOnOutsideClick;
        }
        catch (Exception ex)
        {
            // Closing is the safe choice: a broken panel shouldn't stay on screen.
            LogPanelFailure(panel, nameof(IPanel.ClosesOnOutsideClick), ex);
            return true;
        }
    }

    private void Close(bool returnFocus)
    {
        if (_openPanel is not { } panel)
        {
            return;
        }

        _openPanel = null;
        NotifyClosed(panel);
        EndPanel(returnFocus);
    }

    // Hides the pill once no panel is open and, unless the user clicked away, gives focus back.
    private void EndPanel(bool returnFocus)
    {
        var hadFocus = State.TakesFocus();
        State = PillState.Hidden;
        _view.Hide();
        if (hadFocus && returnFocus)
        {
            _view.ReturnFocus(_focusToReturn);
        }

        _focusToReturn = 0;
        PanelClosed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyClosed(IPanel panel)
    {
        try
        {
            panel.OnClosed();
        }
        catch (Exception ex)
        {
            LogPanelFailure(panel, nameof(IPanel.OnClosed), ex);
        }
    }

    private void LogPanelFailure(IPanel panel, string step, Exception ex)
    {
        // Exception messages can contain file paths, which only go in Debug logs (CLAUDE.md 10.3).
        Logger.Error("Panel {PanelId} threw {Error} in {Step}", panel.Id, ex.GetType().Name, step);
        Logger.Debug(ex, "Panel {PanelId} failed in {Step}", panel.Id, step);
    }
}
