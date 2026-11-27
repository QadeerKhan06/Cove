using System.Collections.Concurrent;
using System.IO;
using Cove.Core.Contracts;
using Serilog;

namespace Cove.Files.Jobs;

/// <summary>
/// Runs file jobs in the background, at most <see cref="MaxConcurrentJobs"/> at a time; the rest wait.
/// Each <see cref="JobKind"/> is done by an <see cref="IJobRunner"/>. This class owns the queue,
/// cancellation and the job events on the event bus.
/// </summary>
public sealed class JobService : IJobService
{
    /// <summary>How many jobs run at once (CLAUDE.md 11.3).</summary>
    public const int MaxConcurrentJobs = 2;

    /// <summary>The Files module's id, used as the source of every job event.</summary>
    private const string SourceId = "files";

    private readonly IEventBus _bus;
    private readonly IReadOnlyDictionary<JobKind, IJobRunner> _runners;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentJobs, MaxConcurrentJobs);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    public JobService(IEventBus bus, IEnumerable<IJobRunner> runners)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(runners);
        _bus = bus;
        _runners = runners.ToDictionary(runner => runner.Kind);
    }

    /// <inheritdoc />
    public Guid Start(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var jobId = Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        _jobs[jobId] = cancellation;

        // Fire and forget on purpose: RunJobAsync catches everything and reports through the bus.
        _ = Task.Run(() => RunJobAsync(jobId, job, cancellation.Token));
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

    private async Task RunJobAsync(Guid jobId, Job job, CancellationToken cancellationToken)
    {
        try
        {
            // Waits here while two other jobs are running. Cancelling while queued ends the wait.
            await _slots.WaitAsync(cancellationToken);
            try
            {
                if (!_runners.TryGetValue(job.Kind, out var runner))
                {
                    throw new NotSupportedException($"No runner registered for {job.Kind} jobs.");
                }

                var progress = new JobProgressReporter(jobId, _bus, SourceId);
                progress.Report(0);
                JobOutput output = await runner.RunAsync(job, progress, cancellationToken);
                PublishCompleted(jobId, job, output);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishCancelled(jobId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Job {JobId} ({Kind}) failed", jobId, job.Kind);
            PublishFailed(jobId, job);
        }
        finally
        {
            // Not disposed: a CancellationTokenSource without timers holds no resources,
            // and disposing it here could race with a late Cancel call.
            _jobs.TryRemove(jobId, out _);
        }
    }

    private void PublishCompleted(Guid jobId, Job job, JobOutput output)
    {
        var result = new JobResult(jobId, true, output.OutputPaths, output.Link, output.CopiedText, null);
        string message = output.OutputPaths.Count switch
        {
            0 => "Done",
            1 => $"Saved {Path.GetFileName(output.OutputPaths[0])}",
            var count => $"Saved {count} files",
        };

        // Some files failed but the rest were saved: still a Completed job, but the user must hear about it.
        // JobResult has no list of failed files (a contract change), so the message is the only place.
        if (output.FailedFiles.Count > 0)
        {
            string failed = output.FailedFiles.Count == 1
                ? Path.GetFileName(output.FailedFiles[0])
                : $"{output.FailedFiles.Count} files";
            message = $"{message}. Couldn't {Verb(job.Kind)} {failed}";
        }

        _bus.Publish(new CoveEvent(JobEventTypes.Completed, SourceId, AlertPriority.JobCompleted, message, result));
    }

    private static string Verb(JobKind kind) => kind switch
    {
        JobKind.Convert => "convert",
        JobKind.Compress => "compress",
        JobKind.Upload => "upload",
        JobKind.CopyText => "read",
        _ => "finish",
    };

    private void PublishFailed(Guid jobId, Job job)
    {
        // TODO(F-10): map exceptions to friendly messages ("Essay.docx is open in Word") with Retry and Details.
        string name = job.InputFiles.Count == 1
            ? Path.GetFileName(job.InputFiles[0])
            : $"{job.InputFiles.Count} files";
        string message = $"Couldn't finish {name}";
        var result = new JobResult(jobId, false, [], null, null, message);
        _bus.Publish(new CoveEvent(JobEventTypes.Failed, SourceId, AlertPriority.JobFailed, message, result));
    }

    private void PublishCancelled(Guid jobId)
    {
        var result = new JobResult(jobId, false, [], null, null, null);

        // No Message: a cancel the user asked for never becomes an Alert.
        _bus.Publish(new CoveEvent(
            Type: JobEventTypes.Cancelled,
            Source: SourceId,
            Priority: AlertPriority.JobCompleted,
            Message: null,
            Data: result));
    }
}