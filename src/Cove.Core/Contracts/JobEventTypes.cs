namespace Cove.Core.Contracts;

/// <summary>
/// Event types the job service publishes on the event bus.
/// Using these constants instead of raw strings avoids typos between modules.
/// </summary>
/// <example>
/// <code>
/// bus.Subscribe(JobEventTypes.Completed, e =&gt; ShowDone((JobResult)e.Data!));
/// </code>
/// </example>
public static class JobEventTypes
{
    /// <summary>A running job made progress. Data: <see cref="JobProgress"/>.</summary>
    public const string Progress = "job.progress";

    /// <summary>A job finished successfully. Data: <see cref="JobResult"/>.</summary>
    public const string Completed = "job.completed";

    /// <summary>A job failed. Data: <see cref="JobResult"/> with <see cref="JobResult.ErrorMessage"/> set.</summary>
    public const string Failed = "job.failed";

    /// <summary>The user cancelled a job. Data: <see cref="JobResult"/>. No failure alert is shown.</summary>
    public const string Cancelled = "job.cancelled";
}