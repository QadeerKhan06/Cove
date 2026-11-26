using Cove.Core.Contracts;
using Serilog;

namespace Cove.Core.Events;

/// <summary>
/// Cove's <see cref="IEventBus"/>. Publish from any thread; every handler runs on the UI thread,
/// in the order it subscribed.
/// </summary>
/// <remarks>
/// <para>
/// Cove.Core can't use WPF (CLAUDE.md 8.1), so the bus reaches the UI thread through that thread's
/// <see cref="SynchronizationContext"/>: .NET's standard way of saying "run this on that thread".
/// In a WPF app, the UI thread's context hands the work to the Dispatcher.
/// </para>
/// <para>
/// A handler that throws is logged and skipped; the other handlers still run. Events published
/// from one thread arrive in the order they were published.
/// </para>
/// </remarks>
public sealed class EventBus : IEventBus
{
    private readonly SynchronizationContext _uiContext;
    private readonly ILogger? _logger;
    private readonly object _lock = new();

    // Type-specific and SubscribeAll subscriptions share one list, so handlers run in the
    // order they subscribed, whichever kind they are.
    private readonly List<Subscription> _subscriptions = new();

    /// <param name="uiContext">
    /// The UI thread's context. Create the bus on the UI thread and pass
    /// <see cref="SynchronizationContext.Current"/>.
    /// </param>
    /// <param name="logger">Where handler failures are logged. Leave it out to use Cove's log; tests pass their own.</param>
    public EventBus(SynchronizationContext uiContext, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(uiContext);
        _uiContext = uiContext;
        _logger = logger;
    }

    // Looked up each time rather than stored, so a bus created before logging starts still logs.
    private ILogger Logger => _logger ?? Log.Logger;

    /// <inheritdoc />
    public void Publish(CoveEvent coveEvent)
    {
        ArgumentNullException.ThrowIfNull(coveEvent);

        Subscription[] matching;
        lock (_lock)
        {
            matching = _subscriptions.Where(subscription => subscription.Matches(coveEvent.Type)).ToArray();
        }

        if (matching.Length == 0)
        {
            return;
        }

        // Always queued, never run here: Publish returns at once on any thread, and a handler
        // that publishes never starts another handler in the middle of its own run.
        _uiContext.Post(_ => Deliver(coveEvent, matching), null);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string eventType, Action<CoveEvent> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        return Add(eventType, handler);
    }

    /// <inheritdoc />
    public IDisposable SubscribeAll(Action<CoveEvent> handler) => Add(eventType: null, handler);

    private Subscription Add(string? eventType, Action<CoveEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, eventType, handler);
        lock (_lock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Remove(Subscription subscription)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscription);
        }
    }

    // Runs on the UI thread.
    private void Deliver(CoveEvent coveEvent, Subscription[] subscriptions)
    {
        foreach (var subscription in subscriptions)
        {
            // Unsubscribed after this event was published. The module may already have released
            // what the handler uses, so it must not get the event.
            if (!subscription.IsActive)
            {
                continue;
            }

            try
            {
                subscription.Handler(coveEvent);
            }
            catch (Exception ex)
            {
                // Exception messages can contain file paths, which only go in Debug logs (CLAUDE.md 10.3).
                Logger.Error(
                    "A handler for {EventType} ({Handler}) threw {Error}; the other handlers still ran",
                    coveEvent.Type,
                    subscription.HandlerName,
                    ex.GetType().Name);
                Logger.Debug(ex, "Handler {Handler} failed on {EventType}", subscription.HandlerName, coveEvent.Type);
            }
        }
    }

    private sealed class Subscription(EventBus bus, string? eventType, Action<CoveEvent> handler) : IDisposable
    {
        // volatile: Dispose can run on any thread, and delivery on the UI thread must see it at once.
        private volatile bool _isActive = true;

        public Action<CoveEvent> Handler => handler;

        public bool IsActive => _isActive;

        // For example "TimerModule.OnQuietStarted", so a log line says whose handler failed.
        public string HandlerName => $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name}";

        // A null type means SubscribeAll: every event matches.
        public bool Matches(string type) => eventType is null || eventType == type;

        public void Dispose()
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            bus.Remove(this);
        }
    }
}
