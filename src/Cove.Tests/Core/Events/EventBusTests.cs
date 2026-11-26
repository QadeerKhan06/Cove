using System.Collections.Concurrent;
using Cove.Core.Contracts;
using Cove.Core.Events;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cove.Tests.Core.Events;

public sealed class EventBusTests : IDisposable
{
    private const string SongChanged = CoveEventTypes.SongChanged;

    private readonly ManualUiContext _ui = new();
    private readonly CapturingSink _logSink = new();
    private readonly Logger _logger;
    private readonly EventBus _bus;

    public EventBusTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_logSink).CreateLogger();
        _bus = new EventBus(_ui, _logger);
    }

    public void Dispose() => _logger.Dispose();

    [Fact]
    public void Handler_GetsEventsOfItsOwnTypeOnly()
    {
        var received = new List<string>();
        _bus.Subscribe(SongChanged, e => received.Add(e.Type));

        _bus.Publish(NewEvent(SongChanged));
        _bus.Publish(NewEvent(CoveEventTypes.TimerFinished));
        _ui.RunAll();

        Assert.Equal(new[] { SongChanged }, received);
    }

    [Fact]
    public void Handlers_RunInTheOrderTheySubscribed_IncludingSubscribeAll()
    {
        var calls = new List<string>();
        _bus.Subscribe(SongChanged, _ => calls.Add("first"));
        _bus.SubscribeAll(_ => calls.Add("everything"));
        _bus.Subscribe(SongChanged, _ => calls.Add("third"));

        _bus.Publish(NewEvent(SongChanged));
        _ui.RunAll();

        Assert.Equal(new[] { "first", "everything", "third" }, calls);
    }

    [Fact]
    public void Events_ArriveInTheOrderTheyWerePublished()
    {
        var received = new List<string?>();
        _bus.SubscribeAll(e => received.Add(e.GetData<string>()));

        for (var i = 1; i <= 5; i++)
        {
            _bus.Publish(NewEvent(SongChanged, $"song {i}"));
        }

        _ui.RunAll();

        Assert.Equal(new string?[] { "song 1", "song 2", "song 3", "song 4", "song 5" }, received);
    }

    [Fact]
    public void Publish_ReturnsBeforeAnyHandlerRuns()
    {
        var ran = false;
        _bus.Subscribe(SongChanged, _ => ran = true);

        _bus.Publish(NewEvent(SongChanged));

        Assert.False(ran); // queued for the UI thread, not run inside Publish
        _ui.RunAll();
        Assert.True(ran);
    }

    [Fact]
    public void Handlers_RunOnTheUiThread_EvenWhenPublishedFromAnotherThread()
    {
        Thread? handlerThread = null;
        _bus.Subscribe(SongChanged, _ => handlerThread = Thread.CurrentThread);
        var publisher = new Thread(() => _bus.Publish(NewEvent(SongChanged)));
        publisher.Start();
        publisher.Join();

        _ui.RunAll(); // in these tests, the test's own thread plays the UI thread

        Assert.Same(Thread.CurrentThread, handlerThread);
        Assert.NotSame(publisher, handlerThread);
    }

    [Fact]
    public void HandlerThatThrows_IsLogged_AndTheOthersStillRun()
    {
        var calls = new List<string>();
        _bus.Subscribe(SongChanged, _ => calls.Add("before"));
        _bus.Subscribe(SongChanged, ThrowingHandler);
        _bus.Subscribe(SongChanged, _ => calls.Add("after"));

        _bus.Publish(NewEvent(SongChanged));
        _bus.Publish(NewEvent(SongChanged)); // a failure must not stop later events either
        _ui.RunAll();

        Assert.Equal(new[] { "before", "after", "before", "after" }, calls);
        var errors = _logSink.Events.Where(e => e.Level == LogEventLevel.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(nameof(ThrowingHandler), errors[0].RenderMessage());
        Assert.Contains(SongChanged, errors[0].RenderMessage());
    }

    [Fact]
    public void AfterDispose_AHandlerGetsNothing_NotEvenAnEventPublishedJustBefore()
    {
        var count = 0;
        var subscription = _bus.Subscribe(SongChanged, _ => count++);

        _bus.Publish(NewEvent(SongChanged)); // queued, not delivered yet
        subscription.Dispose();
        _bus.Publish(NewEvent(SongChanged));
        _ui.RunAll();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ManyThreadsPublishingAndSubscribingAtOnce_LoseNothing()
    {
        const int threads = 8;
        const int eventsPerThread = 1000;
        var received = 0;
        _bus.SubscribeAll(_ => received++); // only runs inside RunAll, on this thread

        var publishers = Enumerable.Range(0, threads).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < eventsPerThread; i++)
            {
                _bus.Publish(NewEvent(SongChanged));

                // Subscribing and unsubscribing at the same time must not break publishing.
                _bus.Subscribe(SongChanged, e => { }).Dispose();
            }
        }));
        await Task.WhenAll(publishers);
        _ui.RunAll();

        Assert.Equal(threads * eventsPerThread, received);
    }

    [Fact]
    public void HandlerThatSubscribesOrUnsubscribesDuringDelivery_DoesNotBreakIt()
    {
        var calls = new List<string>();
        IDisposable? second = null;
        _bus.Subscribe(SongChanged, _ =>
        {
            calls.Add("first");
            second?.Dispose(); // removes a handler that hasn't had its turn yet
            _bus.Subscribe(SongChanged, e => calls.Add("new")); // joins after this event was published
        });
        second = _bus.Subscribe(SongChanged, _ => calls.Add("second"));

        _bus.Publish(NewEvent(SongChanged));
        _ui.RunAll();

        Assert.Equal(new[] { "first" }, calls);
    }

    private static CoveEvent NewEvent(string type, string? data = null) =>
        new(type, "test", AlertPriority.FilesCopied, Data: data);

    private static void ThrowingHandler(CoveEvent coveEvent) =>
        throw new InvalidOperationException("Handler failed");

    /// <summary>
    /// Stands in for the UI thread: posted work waits in a queue until the test calls
    /// <see cref="RunAll"/>, so the test decides exactly when, and on which thread, handlers run.
    /// </summary>
    private sealed class ManualUiContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        // Also runs anything queued while running, such as events published by a handler.
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
