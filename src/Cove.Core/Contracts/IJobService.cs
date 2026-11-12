namespace Cove.Core.Contracts;

/// <summary>
/// Runs file jobs (convert, compress, upload, copy text) in the background.
/// Jobs report progress and results on the event bus (see <see cref="JobEventTypes"/>),
/// so a running job keeps showing in Peek even after its panel closes.
/// </summary>
/// <example>
/// <code>
/// Guid id = jobs.Start(new Job(JobKind.Compress, [path], new Dictionary&lt;string, string&gt; { ["targetMb"] = "10" }));
/// // later, if the user presses Cancel:
/// jobs.Cancel(id);
/// </code>
/// </example>
public interface IJobService
{
    /// <summary>Queues a job and returns its id immediately. At most two jobs run at once.</summary>
    /// <param name="job">The job to run.</param>
    /// <returns>An id used in every event about this job and for <see cref="Cancel"/>.</returns>
    Guid Start(Job job);

    /// <summary>
    /// Cancels a queued or running job, deletes any partial output and publishes
    /// <see cref="JobEventTypes.Cancelled"/>. Does nothing if the job already ended or the id is unknown.
    /// </summary>
    /// <param name="jobId">The id returned by <see cref="Start"/>.</param>
    void Cancel(Guid jobId);
}