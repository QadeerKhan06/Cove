using System.Collections.Concurrent;
using Cove.Core.Contracts;

namespace Cove.App.Fakes;

/// <summary>
/// Pretends to run file jobs, so the pill and Files panel can show progress, success and failure
/// before the real converters exist (card K-11).
/// </summary>
/// <remarks>
/// <para>
/// Each job reports progress in 10 steps over <see cref="DefaultDuration"/>, then publishes
/// <see cref="JobEventTypes.Completed"/>. A job whose options contain
/// <see cref="FailOption"/> = "true" publishes <see cref="JobEventTypes.Failed"/> instead.
/// </para>
/// <para>
/// It never touches any file. Events use the same source, priorities and messages as the real
/// <c>JobService</c>, so UI built against the fake behaves the same with the real one.
/// Unlike the real service, it doesn't limit how many jobs run at once.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// jobs.Start(new Job(JobKind.Convert, files, new Dictionary&lt;string, string&gt; { [FakeJobService.FailOption] = "true" }));
/// </code>
/// </example>
public sealed class FakeJobService : IJobService
{
    /// <summary>Add this option with the value "true" to make a job fail at the end.</summary>
    public const string FailOption = "fake.fail";

    /// <summary>How long a fake job takes ("progress over 5 s", card K-11).</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    // 10% at a time: smooth enough to see the progress bar move.
    private const int ProgressSteps = 10;

    // Same as the real JobService, so the UI can't tell them apart.
    private const string SourceId = "files";

    private readonly IEventBus _bus;
    private readonly TimeSpan _duration;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    /// <summary>Creates a fake job service whose jobs take <see cref="DefaultDuration"/>.</summary>
    public FakeJobService(IEventBus bus)
        : this(bus, DefaultDuration)
    {
    }

    /// <summary>Creates a fake job service with a custom job length. Tests use this so they don't wait.</summary>
    public FakeJobService(IEventBus bus, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _duration = duration;
    }

    /// <inheritdoc />
    public Guid Start(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var jobId = Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        _jobs[jobId] = cancellation;

        // Off the caller's thread, like a real job; Start returns at once.
        _ = Task.Run(() => RunAsync(jobId, job, cancellation.Token));
        return jobId;
    }

    /// <inheritdoc />
    public void Cancel(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private async Task RunAsync(Guid jobId, Job job, CancellationToken cancellationToken)
    {
        try
        {
            for (int step = 1; step <= ProgressSteps; step++)
            {
                await Task.Delay(_duration / ProgressSteps, cancellationToken);
                double percent = 100.0 * step / ProgressSteps;
                _bus.Publish(new CoveEvent(
                    JobEventTypes.Progress,
                    SourceId,
                    AlertPriority.JobCompleted,
                    Message: null,
                    Data: new JobProgress(jobId, percent, "Pretending to convert")));
            }

            if (ShouldFail(job))
            {
                PublishFailed(jobId);
            }
            else
            {
                PublishCompleted(jobId);
            }
        }
        catch (OperationCanceledException)
        {
            PublishCancelled(jobId);
        }
        finally
        {
            // Not disposed: a CancellationTokenSource without timers holds no resources, and
            // disposing it here could race with a Cancel call on another thread.
            _jobs.TryRemove(jobId, out _);
        }
    }

    private static bool ShouldFail(Job job) =>
        job.Options.TryGetValue(FailOption, out string? value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private void PublishCompleted(Guid jobId)
    {
        // No output paths: the fake writes no files, so there's nothing for "Open" to open.
        var result = new JobResult(jobId, true, [], null, null, null);
        _bus.Publish(new CoveEvent(JobEventTypes.Completed, SourceId, AlertPriority.JobCompleted, "Done", result));
    }

    private void PublishFailed(Guid jobId)
    {
        const string message = "Fake job failed, as requested";
        var result = new JobResult(jobId, false, [], null, null, message);
        _bus.Publish(new CoveEvent(JobEventTypes.Failed, SourceId, AlertPriority.JobFailed, message, result));
    }

    private void PublishCancelled(Guid jobId)
    {
        var result = new JobResult(jobId, false, [], null, null, null);

        // No Message: a cancel the user asked for never becomes an Alert (same as the real service).
        _bus.Publish(new CoveEvent(JobEventTypes.Cancelled, SourceId, AlertPriority.JobCompleted, null, result));
    }
}
