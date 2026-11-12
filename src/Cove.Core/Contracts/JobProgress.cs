namespace Cove.Core.Contracts;

/// <summary>A progress update for a running job. Sent as the event data for <see cref="JobEventTypes.Progress"/>.</summary>
/// <param name="JobId">The id returned by <see cref="IJobService.Start"/>.</param>
/// <param name="Percent">How far along the job is, from 0 to 100.</param>
/// <param name="Status">Optional short text for the pill, e.g. "Pass 2 of 2". Null if there's nothing to add.</param>
/// <example>
/// <code>
/// new JobProgress(id, 42.5, "Pass 1 of 2");
/// </code>
/// </example>
public sealed record JobProgress(Guid JobId, double Percent, string? Status);