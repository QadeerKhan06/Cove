using Cove.Core.Contracts;

namespace Cove.Files.Jobs;

/// <summary>
/// Turns a runner's progress reports into <see cref="JobEventTypes.Progress"/> events.
/// Only publishes when the whole-number percent changes, so a fast job doesn't flood the bus.
/// </summary>
internal sealed class JobProgressReporter : IProgress<double>
{
    private readonly Guid _jobId;
    private readonly IEventBus _bus;
    private readonly string _source;
    private readonly object _gate = new();
    private int _lastPublished = -1;

    public JobProgressReporter(Guid jobId, IEventBus bus, string source)
    {
        _jobId = jobId;
        _bus = bus;
        _source = source;
    }

    public void Report(double percent)
    {
        if (double.IsNaN(percent))
        {
            return;
        }

        int whole = (int)Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (whole == _lastPublished)
            {
                return;
            }

            _lastPublished = whole;
        }

        // No Message: progress updates Peek but never becomes an Alert.
        _bus.Publish(new CoveEvent(
            Type: JobEventTypes.Progress,
            Source: _source,
            Priority: AlertPriority.JobCompleted,
            Message: null,
            Data: new JobProgress(_jobId, whole, Status: null)));
    }
}