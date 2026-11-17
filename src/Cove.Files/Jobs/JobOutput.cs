namespace Cove.Files.Jobs;

/// <summary>
/// What a runner produced. <see cref="JobService"/> wraps it in a
/// <see cref="Cove.Core.Contracts.JobResult"/> for the Completed event.
/// </summary>
/// <param name="OutputPaths">Full paths of the files created.</param>
/// <param name="Link">The share link, for upload jobs.</param>
/// <param name="CopiedText">The text that was read, for copy-text jobs.</param>
public sealed record JobOutput(IReadOnlyList<string> OutputPaths, Uri? Link = null, string? CopiedText = null);