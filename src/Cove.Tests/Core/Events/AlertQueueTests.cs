using System.Collections.Concurrent;
using Cove.Core.Contracts;
using Cove.Core.Events;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cove.Tests.Core.Events;

public sealed class AlertQueueTests : IDisposable
{
    private readonly ManualUiContext _ui = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeAlertDisplay _pill = new();
    private readonly CapturingSink _logSink = new();
    private readonly Logger _logger;
    private readonly EventBus _bus;
    private readonly AlertQueue _queue;

    public AlertQueueTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logSink).CreateLogger();
        _bus = new EventBus(_ui, _logger);
        _queue = new AlertQueue(_bus, _pill, _ui, _time, _logger);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _logger.Dispose();
    }

    [Fact]
    public void EventWithAMessage_ShowsAsAnAlert()
    {
        Publish("IMG_2041.heic downloaded");

        Assert.Equal(new[] { "show IMG_2041.heic downloaded" }, _pill.Calls);
    }

    [Fact]
    public void EventWithoutAMessage_IsNeverAnAlert()
    {
        Publish(new CoveEvent(CoveEventTypes.SettingsChanged, "core", AlertPriority.FilesCopied));

        Assert.Empty(_pill.Calls);
    }

    [Fact]
    public void ThreeAlertsInARow_ShowOneAfterAnother_InArrivalOrder()
    {
        Publish("First");
        Publish("Second");
        Publish("Third");
        Assert.Equal(new[] { "show First" }, _pill.Calls);

        Wait(AlertQueue.ShowTime);
        Wait(AlertQueue.ShowTime);
        Wait(AlertQueue.ShowTime);

        // Straight from one to the next; the pill only hides after the last.
        Assert.Equal(new[] { "show First", "show Second", "show Third", "hide" }, _pill.Calls);
    }

    [Fact]
    public void Alert_HidesAfterFourSeconds()
    {
        Publish("Saved Essay.pdf");

        Wait(AlertQueue.ShowTime - TimeSpan.FromMilliseconds(100));
        Assert.DoesNotContain("hide", _pill.Calls);

        Wait(TimeSpan.FromMilliseconds(100));
        Assert.Equal("hide", _pill.Calls[^1]);
    }

    [Fact]
    public void ClickingAButton_RunsItsAction_ThenTheNextAlertShows()
    {
        var runs = 0;
        var convert = new CoveAction("Convert", () => { runs++; return Task.CompletedTask; });
        Publish("IMG_2041.heic downloaded", convert);
        Publish("Saved Essay.pdf");

        _pill.Click(convert);

        Assert.Equal(1, runs);
        Assert.Equal("show Saved Essay.pdf", _pill.Calls[^1]);
    }

    [Fact]
    public void ClickingAButton_OnTheLastAlert_HidesThePill()
    {
        var open = new CoveAction("Open", () => Task.CompletedTask);
        Publish("Saved Essay.pdf", open);

        _pill.Click(open);

        Assert.Equal(new[] { "show Saved Essay.pdf", "hide" }, _pill.Calls);
    }

    [Fact]
    public void ButtonOfAnAlertThatHasGone_DoesNothing()
    {
        var runs = 0;
        var convert = new CoveAction("Convert", () => { runs++; return Task.CompletedTask; });
        Publish("IMG_2041.heic downloaded", convert);
        Wait(AlertQueue.ShowTime);

        _pill.Click(convert);

        Assert.Equal(0, runs);
    }

    [Fact]
    public void ButtonWhoseActionThrows_IsLogged_AndTheQueueMovesOn()
    {
        var broken = new CoveAction("Open", () => throw new InvalidOperationException("Open failed"));
        Publish("Saved Essay.pdf", broken);
        Publish("Low Tide · Marlow Bay");

        _pill.Click(broken);

        Assert.Equal("show Low Tide · Marlow Bay", _pill.Calls[^1]);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void AlertStaysUpWhileItsActionRuns_AndASecondClickDoesNothing()
    {
        // On the UI thread, as in the app, so the action's continuation comes back to it.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_ui);
        try
        {
            var runs = 0;
            var finish = new TaskCompletionSource();
            var upload = new CoveAction("Upload", () => { runs++; return finish.Task; });
            Publish("Saved clip.mp4", upload);

            _pill.Click(upload);
            _pill.Click(upload);
            Wait(AlertQueue.ShowTime);

            Assert.Equal(1, runs);
            Assert.DoesNotContain("hide", _pill.Calls);

            finish.SetResult();
            _ui.RunAll();
            Assert.Equal("hide", _pill.Calls[^1]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AlertsWhileAPanelIsOpen_AreHeld_AndOnlyTheMostImportantShowsWhenItCloses()
    {
        _pill.OpenPanel();
        Publish("Low Tide · Marlow Bay", AlertPriority.SongChanged);
        Publish("IMG_2041.heic downloaded", AlertPriority.DownloadFinished);
        Publish("Night Drive · Marlow Bay", AlertPriority.SongChanged);
        Assert.Empty(_pill.Calls);

        _pill.ClosePanel();
        Wait(AlertQueue.ShowTime);

        Assert.Equal(new[] { "show IMG_2041.heic downloaded", "hide" }, _pill.Calls);
    }

    [Fact]
    public void AmongEquallyImportantHeldAlerts_TheNewestShows()
    {
        _pill.OpenPanel();
        Publish("Low Tide · Marlow Bay", AlertPriority.SongChanged);
        Publish("Night Drive · Marlow Bay", AlertPriority.SongChanged);

        _pill.ClosePanel();

        Assert.Equal(new[] { "show Night Drive · Marlow Bay" }, _pill.Calls);
    }

    [Fact]
    public void HeldAlertOlderThanAMinute_IsDropped()
    {
        _pill.OpenPanel();
        Publish("IMG_2041.heic downloaded", AlertPriority.DownloadFinished);
        Wait(AlertQueue.HeldAlertMaxAge + TimeSpan.FromSeconds(1));
        Publish("Low Tide · Marlow Bay", AlertPriority.SongChanged);

        _pill.ClosePanel();

        Assert.Equal(new[] { "show Low Tide · Marlow Bay" }, _pill.Calls);
    }

    [Theory]
    [InlineData(AlertPriority.JobFailed, "Couldn't convert Essay.docx")]
    [InlineData(AlertPriority.TimerFinished, "Focus done")]
    public void HeldFailureOrTimerDone_StillShows_HoweverOld(AlertPriority priority, string message)
    {
        _pill.OpenPanel();
        Publish(message, priority);
        Wait(TimeSpan.FromMinutes(5));

        _pill.ClosePanel();

        Assert.Equal(new[] { $"show {message}" }, _pill.Calls);
    }

    [Fact]
    public void HeldMicInUse_IsDroppedAfterAMinute()
    {
        // If the mic is still live, the mic module raises the alert again (S-12).
        _pill.OpenPanel();
        Publish("Mic on · Discord", AlertPriority.MicInUse);
        Wait(AlertQueue.HeldAlertMaxAge + TimeSpan.FromSeconds(1));

        _pill.ClosePanel();

        Assert.Empty(_pill.Calls);
    }

    [Fact]
    public void PanelClosing_WithNothingHeld_ShowsNothing()
    {
        _pill.OpenPanel();

        _pill.ClosePanel();

        Assert.Empty(_pill.Calls);
    }

    [Fact]
    public void AlertCutOffByAPanel_IsNotShownAgain_ButOneThatArrivedMeanwhileIs()
    {
        Publish("First");
        _pill.OpenPanel(); // the panel takes the pill's place
        Publish("Second"); // waits behind First, whose countdown is still running
        Wait(AlertQueue.ShowTime); // First's time is up; Second is turned down and held

        _pill.ClosePanel();

        Assert.Equal(new[] { "show First", "show Second" }, _pill.Calls);
    }

    [Fact]
    public void AlertWithTwoButtons_LogsAWarning()
    {
        Publish(
            "Saved Essay.pdf",
            new CoveAction("Open", () => Task.CompletedTask),
            new CoveAction("Show in folder", () => Task.CompletedTask));

        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void AfterDispose_NothingShowsOrHides()
    {
        Publish("First");

        _queue.Dispose();
        Publish("Second");
        Wait(AlertQueue.ShowTime);

        Assert.Equal(new[] { "show First" }, _pill.Calls);
    }

    private void Publish(string message, params CoveAction[] actions) =>
        Publish(new CoveEvent(CoveEventTypes.FileArrived, "test", AlertPriority.DownloadFinished, message) { Actions = actions });

    private void Publish(string message, AlertPriority priority) =>
        Publish(new CoveEvent("test.alert", "test", priority, message));

    private void Publish(CoveEvent coveEvent)
    {
        _bus.Publish(coveEvent);
        _ui.RunAll();
    }

    private void Wait(TimeSpan time)
    {
        _time.Advance(time);
        _ui.RunAll();
    }

    /// <summary>
    /// Stands in for the pill: records what the queue asks of it, and turns alerts down while a
    /// "panel" is open, as the real pill does.
    /// </summary>
    private sealed class FakeAlertDisplay : IAlertDisplay
    {
        private bool _panelOpen;
        private bool _alertShowing;

        public event EventHandler? PanelClosed;

        public event EventHandler<CoveAction>? ActionClicked;

        public List<string> Calls { get; } = new();

        public bool ShowAlert(CoveEvent alert)
        {
            if (_panelOpen)
            {
                return false;
            }

            Calls.Add($"show {alert.Message}");
            _alertShowing = true;
            return true;
        }

        public void HideAlert()
        {
            if (!_alertShowing)
            {
                return;
            }

            Calls.Add("hide");
            _alertShowing = false;
        }

        // Like the real pill, a panel takes the place of any alert on screen.
        public void OpenPanel()
        {
            _panelOpen = true;
            _alertShowing = false;
        }

        public void ClosePanel()
        {
            _panelOpen = false;
            PanelClosed?.Invoke(this, EventArgs.Empty);
        }

        public void Click(CoveAction action) => ActionClicked?.Invoke(this, action);
    }

    /// <summary>A clock that only moves when the test says so, with timers that fire as it passes them.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan time)
        {
            _now += time;
            foreach (var timer in _timers.Where(timer => timer.IsDue(_now)).ToList())
            {
                timer.Fire();
            }
        }

        // The queue's timers fire once and are never changed, only replaced.
        private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
        {
            private bool _done;

            public bool IsDue(DateTimeOffset now) => !_done && now >= dueAt;

            public void Fire()
            {
                _done = true;
                callback(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                throw new NotSupportedException("The alert queue never changes a timer.");

            public void Dispose() => _done = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Stands in for the UI thread: posted work waits until the test calls <see cref="RunAll"/>.
    /// </summary>
    private sealed class ManualUiContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunAll()
        {
            while (_queue.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }

    /// <summary>Keeps every log event in memory so tests can check what was logged.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
