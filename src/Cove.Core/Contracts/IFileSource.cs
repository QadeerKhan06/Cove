namespace Cove.Core.Contracts;

/// <summary>
/// Finds the files the user most likely wants to work on when they open the Files panel.
/// </summary>
/// <example>
/// <code>
/// var files = fileSource.GetCurrentFiles();
/// if (files.Count == 0) ShowDropZone();
/// </code>
/// </example>
public interface IFileSource
{
    /// <summary>
    /// Returns, in order of preference: the files selected in the Explorer window that was in front
    /// when the shortcut was pressed; otherwise files copied in the last 2 minutes; otherwise an empty list.
    /// Call this <b>before</b> Cove's panel takes focus, or the Explorer window is no longer in front.
    /// Never throws for non-Explorer apps; returns an empty list instead.
    /// </summary>
    /// <returns>Full file paths, or an empty list.</returns>
    IReadOnlyList<string> GetCurrentFiles();
}