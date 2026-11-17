using Cove.Core.Contracts;

namespace Cove.Files.Jobs;

/// <summary>
/// Does the actual work for one <see cref="JobKind"/>, e.g. the image converter in F-03.
/// <see cref="JobService"/> handles queueing, cancellation and events; a runner only does the work.
/// </summary>
public interface IJobRunner
{
    /// <summary>The kind of job this runner handles. One runner per kind.</summary>
    JobKind Kind { get; }

    /// <summary>
    /// Runs <paramref name="job"/> and returns what it produced.
    /// Report progress from 0 to 100 through <paramref name="progress"/>.
    /// Check <paramref name="cancellationToken"/> often and let <see cref="OperationCanceledException"/>
    /// escape when it's cancelled. Throw on failure; <see cref="JobService"/> turns it into a Failed event.
    /// </summary>
    Task<JobOutput> RunAsync(Job job, IProgress<double> progress, CancellationToken cancellationToken);
}