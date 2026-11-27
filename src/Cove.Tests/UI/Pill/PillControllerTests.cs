using System.Collections.Concurrent;
using Cove.Core.Contracts;
using Cove.UI.Pill;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cove.Tests.UI.Pill;

public sealed class PillControllerTests : IDisposable
{
    // Stand-in window handles: the app the user was typing in, the pill once it has focus, and
    // another app the user clicks into while a panel is open.
    private const nint WordWindow = 1001;
    private const nint PillHandle = 2002;
    private const nint ExplorerWindow = 3003;

    private readonly FakePillView _view = new();
    private readonly CapturingSink _logSink = new();
    private readonly Logger _logger;

    public PillControllerTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logSink).CreateLogger();
    }

    public void Dispose() => _logger.Dispose();

    [Fact]
    public void Starts_Hidden()
    {
        var pill = NewController(new FakePanel("files"));

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Empty(_view.Calls);
    }

    [Fact]
    public void OpenPanel_RemembersFocusFirst_ThenShowsTheView_AndPassesTheParameter()
    {
        var files = new FakePanel("files");
        var pill = NewController(files);
        var parameter = new[] { @"C:\photo.heic" };

        pill.OpenPanel("files", parameter);

        Assert.Equal(PillState.Panel, pill.State);
        Assert.Equal(new[] { "foreground", "panel files title" }, _view.Calls);
        Assert.Same(files.View, _view.ShownView);
        Assert.Same(parameter, files.OpenedWith);
    }

    [Fact]
    public void ClosePanel_HidesAndGivesFocusBackToTheWindowThatHadIt()
    {
        var files = new FakePanel("files");
        var pill = NewController(files);
        pill.OpenPanel("files");
        _view.Calls.Clear();

        pill.ClosePanel();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal(new[] { "hide", $"focus {WordWindow}" }, _view.Calls);
        Assert.Equal(new[] { "create", "opened", "closed" }, files.Calls);
    }

    [Fact]
    public void Escape_ClosesThePanel_AndGivesFocusBack()
    {
        var pill = NewController(new FakePanel("files"));
        pill.OpenPanel("files");
        _view.Calls.Clear();

        _view.PressEscape();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal(new[] { "hide", $"focus {WordWindow}" }, _view.Calls);
    }

    [Fact]
    public void ClickOutside_ClosesThePanel_WithoutMovingFocus()
    {
        var files = new FakePanel("files");
        var pill = NewController(files);
        pill.OpenPanel("files");
        _view.Calls.Clear();

        _view.ClickOutside();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal(new[] { "hide" }, _view.Calls);
        Assert.Contains("closed", files.Calls);
    }

    [Fact]
    public void PanelThatWaitsForInput_StaysOpenOnAClickOutside()
    {
        var dropZone = new FakePanel("files") { ClosesOnOutsideClick = false };
        var pill = NewController(dropZone);
        pill.OpenPanel("files");
        _view.Calls.Clear();

        _view.ClickOutside(ExplorerWindow);

        Assert.Equal(PillState.Panel, pill.State);
        Assert.DoesNotContain("hide", _view.Calls);
        Assert.DoesNotContain("closed", dropZone.Calls);
    }

    [Fact]
    public void PanelThatWaitsForInput_ClosedAfterUsingAnotherApp_GivesFocusToThatApp()
    {
        var pill = NewController(new FakePanel("files") { ClosesOnOutsideClick = false });
        pill.OpenPanel("files");
        _view.ClickOutside(ExplorerWindow);

        _view.PressEscape();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal($"focus {ExplorerWindow}", _view.Calls[^1]);
    }

    [Fact]
    public void PanelThatThrowsInClosesOnOutsideClick_IsClosed()
    {
        var pill = NewController(new FakePanel("files") { FailIn = nameof(IPanel.ClosesOnOutsideClick) });
        pill.OpenPanel("files");

        _view.ClickOutside();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void ClickOutside_AfterThePanelClosed_IsIgnored()
    {
        // Giving focus back makes the pill lose focus, which the window reports as a click outside.
        var pill = NewController(new FakePanel("files"));
        pill.OpenPanel("files");
        pill.ClosePanel();
        _view.Calls.Clear();

        _view.ClickOutside();

        Assert.Empty(_view.Calls);
    }

    [Fact]
    public void OpeningASecondPanel_SwapsThem_AndFocusStillGoesBackToTheOriginalApp()
    {
        var files = new FakePanel("files", 'D');
        var timer = new FakePanel("timer", 'T');
        var pill = NewController(files, timer);
        pill.OpenPanel("files");

        pill.OpenPanel("timer");
        pill.ClosePanel();

        Assert.Equal(new[] { "create", "opened", "closed" }, files.Calls);
        Assert.Equal(new[] { "create", "opened", "closed" }, timer.Calls);
        Assert.Single(_view.Calls, call => call == "foreground");
        Assert.Equal($"focus {WordWindow}", _view.Calls[^1]);
    }

    [Fact]
    public void SwappingPanels_DoesNotRaisePanelClosed_ButClosingDoes()
    {
        var pill = NewController(new FakePanel("files", 'D'), new FakePanel("timer", 'T'));
        var closedCount = 0;
        pill.PanelClosed += (_, _) => closedCount++;

        pill.OpenPanel("files");
        pill.OpenPanel("timer");
        Assert.Equal(0, closedCount);

        _view.ClickOutside();
        Assert.Equal(1, closedCount);
    }

    [Fact]
    public void ClosePanel_WithNothingOpen_DoesNothing()
    {
        var pill = NewController(new FakePanel("files"));
        var closedCount = 0;
        pill.PanelClosed += (_, _) => closedCount++;

        pill.ClosePanel();

        Assert.Empty(_view.Calls);
        Assert.Equal(0, closedCount);
    }

    [Fact]
    public void OpenPanel_WithUnknownId_DoesNothing_AndLogsAWarning()
    {
        var pill = NewController(new FakePanel("files"));

        pill.OpenPanel("shelf");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Empty(_view.Calls);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public void DuplicateId_KeepsTheFirstPanel()
    {
        var first = new FakePanel("files", 'D');
        var second = new FakePanel("files", 'F');
        var pill = NewController(first, second);

        pill.OpenPanel("files");

        Assert.Same(first.View, _view.ShownView);
        Assert.Empty(second.Calls);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void DuplicateMenuLetter_SkipsTheSecondPanel_IgnoringCase()
    {
        var pill = NewController(new FakePanel("files", 'D'), new FakePanel("downloads", 'd'));

        pill.OpenPanel("downloads");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void PanelThatThrowsInCreateView_IsNotOpened_AndCoveCarriesOn()
    {
        var pill = NewController(new FakePanel("files") { FailIn = nameof(IPanel.CreateView) });

        pill.OpenPanel("files");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.DoesNotContain(_view.Calls, call => call.StartsWith("panel", StringComparison.Ordinal));
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void ViewTheWindowCannotShow_IsNotOpened()
    {
        _view.AcceptsViews = false;
        var files = new FakePanel("files");
        var pill = NewController(files);

        pill.OpenPanel("files");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.DoesNotContain("opened", files.Calls);
        Assert.DoesNotContain(_view.Calls, call => call.StartsWith("focus", StringComparison.Ordinal));
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void PanelThatThrowsInOnOpened_IsClosed_AndFocusGoesBack()
    {
        var pill = NewController(new FakePanel("files") { FailIn = nameof(IPanel.OnOpened) });

        pill.OpenPanel("files");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal($"focus {WordWindow}", _view.Calls[^1]);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void PanelThatThrowsInOnClosed_StillCloses()
    {
        var pill = NewController(new FakePanel("files") { FailIn = nameof(IPanel.OnClosed) });
        pill.OpenPanel("files");

        pill.ClosePanel();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal($"focus {WordWindow}", _view.Calls[^1]);
        Assert.Contains(_logSink.Events, e => e.Level == LogEventLevel.Error);
    }

    [Fact]
    public void FailedSwap_HidesThePill_AndGivesFocusBack()
    {
        var pill = NewController(
            new FakePanel("files", 'D'),
            new FakePanel("timer", 'T') { FailIn = nameof(IPanel.CreateView) });
        pill.OpenPanel("files");

        pill.OpenPanel("timer");

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal(new[] { "hide", $"focus {WordWindow}" }, _view.Calls.TakeLast(2));
    }

    [Fact]
    public void ShowAlert_ThenHideAlert()
    {
        var pill = NewController();

        Assert.True(pill.ShowAlert(Alert("IMG_2041.heic downloaded")));
        Assert.Equal(PillState.Alert, pill.State);

        pill.HideAlert();
        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal(new[] { "alert IMG_2041.heic downloaded", "hide" }, _view.Calls);
    }

    [Fact]
    public void ShowAlert_WhileAPanelIsOpen_IsTurnedDown()
    {
        var pill = NewController(new FakePanel("files"));
        pill.OpenPanel("files");

        Assert.False(pill.ShowAlert(Alert("Saved Essay.pdf")));
        Assert.Equal(PillState.Panel, pill.State);
        Assert.DoesNotContain(_view.Calls, call => call.StartsWith("alert", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningAPanelOverAnAlert_ReplacesIt_AndRemembersTheAppUnderneath()
    {
        var pill = NewController(new FakePanel("files"));
        pill.ShowAlert(Alert("IMG_2041.heic downloaded"));

        pill.OpenPanel("files");
        pill.ClosePanel();

        Assert.Equal(PillState.Hidden, pill.State);
        Assert.Equal($"focus {WordWindow}", _view.Calls[^1]);
    }

    [Fact]
    public void ClickOutside_WhileAnAlertShows_IsIgnored()
    {
        var pill = NewController();
        pill.ShowAlert(Alert("Low Tide · Marlow Bay"));

        _view.ClickOutside();

        Assert.Equal(PillState.Alert, pill.State);
    }

    [Fact]
    public void AlertButtonClick_IsPassedOnToTheAlertQueue()
    {
        var pill = NewController();
        var convert = new CoveAction("Convert", () => Task.CompletedTask);
        CoveAction? clicked = null;
        pill.ActionClicked += (_, action) => clicked = action;
        pill.ShowAlert(Alert("IMG_2041.heic downloaded") with { Actions = [convert] });

        _view.ClickAlertButton(convert);

        Assert.Same(convert, clicked);
    }

    [Fact]
    public void AlertButtonClick_AfterAPanelReplacedTheAlert_IsIgnored()
    {
        var pill = NewController(new FakePanel("files"));
        var convert = new CoveAction("Convert", () => Task.CompletedTask);
        var clicks = 0;
        pill.ActionClicked += (_, _) => clicks++;
        pill.ShowAlert(Alert("IMG_2041.heic downloaded") with { Actions = [convert] });
        pill.OpenPanel("files");

        _view.ClickAlertButton(convert);

        Assert.Equal(0, clicks);
    }

    private static CoveEvent Alert(string message) =>
        new(CoveEventTypes.FileArrived, "test", AlertPriority.DownloadFinished, message);

    private PillController NewController(params IPanel[] panels) => new(_view, panels, _logger);

    /// <summary>
    /// Records what the controller asks of the window. Showing a panel moves focus to the pill,
    /// as the real window does, so tests can tell the original app from the pill.
    /// </summary>
    private sealed class FakePillView : IPillView
    {
        private nint _foreground = WordWindow;

        public event EventHandler? EscapePressed;

        public event EventHandler? ClickedOutside;

        public event EventHandler<CoveAction>? AlertActionClicked;

        public List<string> Calls { get; } = new();

        public bool AcceptsViews { get; set; } = true;

        public object? ShownView { get; private set; }

        public nint GetForegroundWindow()
        {
            Calls.Add("foreground");
            return _foreground;
        }

        public void ShowAlert(CoveEvent alert) => Calls.Add($"alert {alert.Message}");

        public bool ShowPanel(string title, object view)
        {
            if (!AcceptsViews)
            {
                return false;
            }

            Calls.Add($"panel {title}");
            ShownView = view;
            _foreground = PillHandle;
            return true;
        }

        public void Hide() => Calls.Add("hide");

        public void ReturnFocus(nint window)
        {
            Calls.Add($"focus {window}");
            _foreground = window;
        }

        public void PressEscape() => EscapePressed?.Invoke(this, EventArgs.Empty);

        public void ClickAlertButton(CoveAction action) => AlertActionClicked?.Invoke(this, action);

        // Like Windows, the clicked window has focus by the time the pill hears about it.
        public void ClickOutside(nint window = ExplorerWindow)
        {
            _foreground = window;
            ClickedOutside?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A panel that records its calls and can throw in one step, like a buggy module.</summary>
    private sealed class FakePanel(string id, char menuLetter = 'X') : IPanel
    {
        private readonly bool _closesOnOutsideClick = true;

        public string Id { get; } = id;

        public bool ClosesOnOutsideClick
        {
            get
            {
                ThrowIfFailing(nameof(ClosesOnOutsideClick));
                return _closesOnOutsideClick;
            }

            init => _closesOnOutsideClick = value;
        }

        public string Title => $"{Id} title";

        public char MenuLetter { get; } = menuLetter;

        public object View { get; } = new();

        public object? OpenedWith { get; private set; }

        public List<string> Calls { get; } = new();

        public string? FailIn { get; init; }

        public object CreateView()
        {
            Calls.Add("create");
            ThrowIfFailing(nameof(CreateView));
            return View;
        }

        public void OnOpened(object? parameter)
        {
            Calls.Add("opened");
            OpenedWith = parameter;
            ThrowIfFailing(nameof(OnOpened));
        }

        public void OnClosed()
        {
            Calls.Add("closed");
            ThrowIfFailing(nameof(OnClosed));
        }

        private void ThrowIfFailing(string step)
        {
            if (FailIn == step)
            {
                throw new InvalidOperationException($"{step} failed");
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
