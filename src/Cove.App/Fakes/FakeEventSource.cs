using Cove.Core.Contracts;

namespace Cove.App.Fakes;

/// <summary>
/// Publishes a fake song change and a fake finished download every <see cref="DefaultInterval"/>,
/// so the pill's Alerts can be built before Now Playing and the download watcher exist (card K-11).
/// </summary>
/// <remarks>
/// The events carry text in <see cref="CoveEvent.Message"/> only. There's no shared song or
/// download data type in the contracts yet, and adding one is a contract change.
/// Call <see cref="Start"/> to begin and <see cref="Dispose"/> to stop.
/// </remarks>
public sealed class FakeEventSource : IDisposable
{
    /// <summary>How often both events are published ("every 10 s", card K-11).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    // Not a real module id, so logs show clearly that these events are fake.
    private const string SourceId = "fakes";

    private readonly IEventBus _bus;
    private readonly TimeSpan _interval;
    private readonly object _lock = new();
    private ITimer? _timer;
    private int _count;

    /// <summary>Creates a fake event source that publishes every <see cref="DefaultInterval"/>.</summary>
    public FakeEventSource(IEventBus bus)
        : this(bus, DefaultInterval)
    {
    }

    /// <summary>Creates a fake event source with a custom interval. Tests use this so they don't wait.</summary>
    public FakeEventSource(IEventBus bus, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _interval = interval;
    }

    /// <summary>Starts publishing. The first pair of events comes after one interval. Calling it again does nothing.</summary>
    public void Start()
    {
        lock (_lock)
        {
            _timer ??= TimeProvider.System.CreateTimer(_ => PublishBoth(), null, _interval, _interval);
        }
    }

    /// <summary>Stops publishing.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void PublishBoth()
    {
        int number = Interlocked.Increment(ref _count);

        _bus.Publish(new CoveEvent(
            CoveEventTypes.SongChanged,
            SourceId,
            AlertPriority.SongChanged,
            Message: $"Fake Song {number} · Test Artist"));

        _bus.Publish(new CoveEvent(
            CoveEventTypes.FileArrived,
            SourceId,
            AlertPriority.DownloadFinished,
            Message: $"Downloaded fake-download-{number}.zip"));
    }
}
