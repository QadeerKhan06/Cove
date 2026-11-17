using Cove.Core.Contracts;

namespace Cove.Tests.Files.Jobs;

/// <summary>An in-memory event bus for tests: records every event and calls handlers right away.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly List<CoveEvent> _events = new();
    private readonly List<(string? Type, Action<CoveEvent> Handler)> _handlers = new();

    /// <summary>A snapshot of every event published so far.</summary>
    public IReadOnlyList<CoveEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToList();
            }
        }
    }

    public void Publish(CoveEvent coveEvent)
    {
        List<Action<CoveEvent>> toCall;
        lock (_gate)
        {
            _events.Add(coveEvent);
            toCall = _handlers
                .Where(h => h.Type is null || h.Type == coveEvent.Type)
                .Select(h => h.Handler)
                .ToList();
        }

        foreach (var handler in toCall)
        {
            handler(coveEvent);
        }
    }

    public IDisposable Subscribe(string eventType, Action<CoveEvent> handler) => Add(eventType, handler);

    public IDisposable SubscribeAll(Action<CoveEvent> handler) => Add(null, handler);

    private IDisposable Add(string? eventType, Action<CoveEvent> handler)
    {
        var entry = (eventType, handler);
        lock (_gate)
        {
            _handlers.Add(entry);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                _handlers.Remove(entry);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}