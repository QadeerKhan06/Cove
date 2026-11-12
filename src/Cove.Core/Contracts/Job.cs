
namespace Cove.Core.Contracts;

/// <summary>
/// A piece of background work for the file engine: what to do, to which files, and how.
/// </summary>
/// <param name="Kind">What the job does.</param>
/// <param name="InputFiles">Full paths of the files to work on. Originals are never modified; output goes next to them.</param>
/// <param name="Options">
/// Kind-specific settings, e.g. <c>format = jpg</c> for Convert or <c>targetMb = 10</c> for Compress.
/// Empty means use the defaults.
/// </param>
/// <example>
/// <code>
/// var job = new Job(
///     JobKind.Convert,
///     ["C:\\Photos\\IMG_001.heic"],
///     new Dictionary&lt;string, string&gt; { ["format"] = "jpg" });
/// </code>
/// </example>
public sealed record Job(
    JobKind Kind,
    IReadOnlyList<string> InputFiles,
    IReadOnlyDictionary<string, string> Options);