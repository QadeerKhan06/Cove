namespace Cove.Core.Contracts;

/// <summary>
/// The outcome of a finished job. Sent as the event data for
/// <see cref="JobEventTypes.Completed"/>, <see cref="JobEventTypes.Failed"/> and <see cref="JobEventTypes.Cancelled"/>.
/// </summary>
/// <param name="JobId">The id returned by <see cref="IJobService.Start"/>.</param>
/// <param name="Succeeded">True if the job finished without errors.</param>
/// <param name="OutputPaths">Full paths of the files created. Empty for Upload and CopyText, or on failure.</param>
/// <param name="Link">The OneDrive share link, for Upload jobs only.</param>
/// <param name="CopiedText">The text that was read, for CopyText jobs only.</param>
/// <param name="ErrorMessage">A plain-language reason for the user, e.g. "Essay.docx is open in Word". Null on success.</param>
/// <example>
/// <code>
/// new JobResult(id, true, ["C:\\Photos\\IMG_001.jpg"], null, null, null);
/// </code>
/// </example>
public sealed record JobResult(
    Guid JobId,
    bool Succeeded,
    IReadOnlyList<string> OutputPaths,
    Uri? Link,
    string? CopiedText,
    string? ErrorMessage);