using Cove.Core.Contracts;
using Serilog;

namespace Cove.Core.Events;

/// <summary>
/// Turns events that carry a <see cref="CoveEvent.Message"/> into alerts on the pill, one at a time.
/// </summary>
/// <remarks>
/// <para>
/// Alerts show in the order they arrive, each for <see cref="ShowTime"/>; when one ends and another
/// is waiting, the pill changes straight to it. C-11 adds priority order, merging and longer times
/// for urgent alerts; S-15 adds quiet time.
/// </para>
/// <para>
/// While a panel is open the pill turns alerts down, and they're held. When the panel closes, at
/// most one shows ("one thing at a time", SPEC.md §1): the most important wins, and the newest among
/// equals. Held alerts older than <see cref="HeldAlertMaxAge"/> are dropped, except failures and
/// "timer done", since a module can't take an alert back yet (K-08).
/// </para>
/// <para>Use on the UI thread. Timer callbacks are posted back to it, as the event bus does.</para>
/// </remarks>
public sealed class AlertQueue : IDisposable
{
    /// <summary>How long an alert stays up (SPEC.md §5).</summary>
    public static readonly TimeSpan ShowTime = TimeSpan.FromSeconds(4);

    /// <summary>
    /// A held alert older than this is stale, e.g. a download the user already dealt with in the
    /// panel. Decided by the Lead on 2026-11-27, until K-08 lets modules withdraw alerts.
    /// </summary>
    public static readonly TimeSpan HeldAlertMaxAge = TimeSpan.FromSeconds(60);

    private readonly IAlertDisplay _display;
    private readonly SynchronizationContext _uiContext;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly IDisposable _subscription;

    // Alerts waiting for the one on screen to finish, and alerts turned down while a panel was open.
    private readonly Queue<Pending> _waiting = new();
    private readonly List<Pending> _held = new();

    private CoveEvent? _current;
    private ITimer? _hideTimer;
    private bool _actionRunning;
    private bool _disposed;

    /// <param name="bus">Where alert events arrive. The queue subscribes to every event.</param>
    /// <param name="display">The pill.</param>
    /// <param name="uiContext">The UI thread's context; pass <see cref="SynchronizationContext.Current"/> on the UI thread.</param>
    /// <param name="time">The clock and timers. Leave it out for the real ones; tests pass a fake.</param>
    /// <param name="logger">Where failures are logged. Leave it out to use Cove's log; tests pass their own.</param>
    public AlertQueue(
        IEventBus bus,
        IAlertDisplay display,
        SynchronizationContext uiContext,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(uiContext);
        _display = display;
        _uiContext = uiContext;
        _time = time ?? TimeProvider.System;
        _logger = logger;

        _display.PanelClosed += OnPanelClosed;
        _display.ActionClicked += OnActionClicked;

        // SubscribeAll is meant for the alert queue and logging (IEventBus).
        _subscription = bus.SubscribeAll(OnEvent);
    }

    // Looked up each time rather than stored, so a queue created before logging starts still logs.
    private ILogger Logger => _logger ?? Log.Logger;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
        _display.PanelClosed -= OnPanelClosed;
        _display.ActionClicked -= OnActionClicked;
        _hideTimer?.Dispose();
        _waiting.Clear();
        _held.Clear();
    }

    // Runs on the UI thread (the bus delivers there).
    private void OnEvent(CoveEvent coveEvent)
    {
        // An event without a message is never an alert (CoveEvent's contract).
        if (_disposed || coveEvent.Message is null)
        {
            return;
        }

        if (coveEvent.Actions.Count > 1)
        {
            // The design allows one action per alert (SPEC.md §7); the contract allows two until K-08.
            Logger.Warning(
                "Alert {EventType} from {Source} has {Count} buttons; the pill shows only the first",
                coveEvent.Type,
                coveEvent.Source,
                coveEvent.Actions.Count);
        }

        var pending = new Pending(coveEvent, _time.GetUtcNow());
        if (_current is null)
        {
            Show(pending);
        }
        else
        {
            _waiting.Enqueue(pending);
        }
    }

    // Shows the alert, or holds it while a panel is open.
    private void Show(Pending pending)
    {
        if (!_display.ShowAlert(pending.Alert))
        {
            _held.Add(pending);
            return;
        }

        _current = pending.Alert;
        StartHideTimer(pending.Alert);
    }

    private void StartHideTimer(CoveEvent alert)
    {
        _hideTimer?.Dispose();

        // TimeProvider timers fire on a thread-pool thread, and the pill must only be touched on the UI thread.
        _hideTimer = _time.CreateTimer(
            _ => _uiContext.Post(_ => OnShowTimeUp(alert), null),
            null,
            ShowTime,
            Timeout.InfiniteTimeSpan);
    }

    private void OnShowTimeUp(CoveEvent alert)
    {
        // Reference, not record equality: two identical events are still two alerts. While a button's
        // action runs, the alert stays up until the action finishes.
        if (_disposed || _actionRunning || !ReferenceEquals(alert, _current))
        {
            return;
        }

        ShowNext();
    }

    // Moves on from the current alert: straight to the next waiting one, or hides the pill.
    private void ShowNext()
    {
        _current = null;
        _hideTimer?.Dispose();
        _hideTimer = null;

        // If a panel opened meanwhile, each of these is turned down and held instead.
        while (_current is null && _waiting.TryDequeue(out var next))
        {
            Show(next);
        }

        if (_current is null)
        {
            _display.HideAlert();
        }
    }

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        if (_disposed || _held.Count == 0)
        {
            return;
        }

        // Newest first, so MaxBy picks the newest of equally important alerts.
        var now = _time.GetUtcNow();
        var best = Enumerable.Reverse(_held)
            .Where(pending => NeverGoesStale(pending.Alert) || now - pending.ReceivedAt <= HeldAlertMaxAge)
            .MaxBy(pending => pending.Alert.Priority);

        Logger.Debug("A panel closed with {Count} alerts held; showing at most one", _held.Count);
        _held.Clear();
        if (best is null)
        {
            return;
        }

        // An alert a panel opened over may still be counting down; the held one takes its place.
        _hideTimer?.Dispose();
        _hideTimer = null;
        _current = null;
        Show(best);
    }

    // A button runs on the UI thread (CoveAction's contract). async void because it's an event
    // handler, so everything in it is caught and logged (CLAUDE.md 10.2).
    private async void OnActionClicked(object? sender, CoveAction action)
    {
        try
        {
            var alert = _current;
            if (_disposed || _actionRunning || alert is null || !alert.Actions.Any(a => ReferenceEquals(a, action)))
            {
                return;
            }

            // The alert stays up while its action runs; a second click does nothing.
            _actionRunning = true;
            _hideTimer?.Dispose();
            _hideTimer = null;
            try
            {
                await action.InvokeAsync();
            }
            catch (Exception ex)
            {
                // Exception messages can contain file paths, which only go in Debug logs (CLAUDE.md 10.3).
                Logger.Error(
                    "The {Label} button on alert {EventType} threw {Error}",
                    action.Label,
                    alert.Type,
                    ex.GetType().Name);
                Logger.Debug(ex, "Alert button {Label} failed", action.Label);
            }
            finally
            {
                _actionRunning = false;
            }

            if (!_disposed && ReferenceEquals(alert, _current))
            {
                ShowNext();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Moving on after an alert button failed ({Error})", ex.GetType().Name);
            Logger.Debug(ex, "Alert queue failure after a button click");
        }
    }

    // Worth showing however long the panel was open (Lead, 2026-11-27). "Mic in use" isn't: if the
    // mic is still live, the mic module raises it again.
    private static bool NeverGoesStale(CoveEvent alert) =>
        alert.Priority is AlertPriority.JobFailed or AlertPriority.TimerFinished;

    private sealed record Pending(CoveEvent Alert, DateTimeOffset ReceivedAt);
}
