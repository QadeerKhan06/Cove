using System.IO;
using Serilog;

namespace Cove.Files.Jobs;

/// <summary>
/// Writes a job's output file without ever overwriting anything (CLAUDE.md 10.6).
/// The runner writes to <see cref="TempPath"/>; <see cref="Commit"/> renames it to a free name next to
/// the original ("photo.jpg", else "photo (1).jpg", ...). Disposing without committing (cancel or
/// failure) deletes the temporary file, so nothing is left behind.
/// </summary>
/// <example>
/// <code>
/// using var output = SafeOutputFile.Create(@"C:\Photos\IMG_001.heic", ".jpg");
/// image.Write(output.TempPath);
/// string savedAs = output.Commit(); // C:\Photos\IMG_001.jpg, or "IMG_001 (1).jpg" if that's taken
/// </code>
/// </example>
public sealed class SafeOutputFile : IDisposable
{
    // How many " (n)" names to try before giving up. Far more than any real folder needs.
    private const int MaxNameAttempts = 10_000;

    private readonly string _desiredPath;
    private bool _committed;

    private SafeOutputFile(string desiredPath, string tempPath)
    {
        _desiredPath = desiredPath;
        TempPath = tempPath;
    }

    /// <summary>Where the runner should write. Close the file before calling <see cref="Commit"/>.</summary>
    public string TempPath { get; }

    /// <summary>
    /// Plans an output for <paramref name="inputPath"/> with a new extension, e.g. ".jpg".
    /// Output goes next to the input unless <paramref name="outputFolder"/> is given.
    /// </summary>
    public static SafeOutputFile Create(string inputPath, string newExtension, string? outputFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newExtension);

        string extension = newExtension.StartsWith('.') ? newExtension : "." + newExtension;
        string folder = outputFolder
            ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))
            ?? throw new ArgumentException("The input path has no folder.", nameof(inputPath));
        string baseName = Path.GetFileNameWithoutExtension(inputPath);

        // Same folder as the output, so the final rename is instant and never copies across drives.
        // Keeps the real extension at the end: Magick.NET and ffmpeg pick the format from it.
        string uniquePart = Guid.NewGuid().ToString("N")[..8];
        string tempPath = Path.Combine(folder, $"{baseName}.cove-partial-{uniquePart}{extension}");

        return new SafeOutputFile(Path.Combine(folder, baseName + extension), tempPath);
    }

    /// <summary>
    /// Returns <paramref name="desiredPath"/> if nothing has that name, otherwise the first free
    /// "name (1).ext", "name (2).ext", ...
    /// </summary>
    public static string GetFreePath(string desiredPath)
    {
        if (!IsTaken(desiredPath))
        {
            return desiredPath;
        }

        string folder = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);
        for (int number = 1; number <= MaxNameAttempts; number++)
        {
            string candidate = Path.Combine(folder, $"{name} ({number}){extension}");
            if (!IsTaken(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Couldn't find a free name for {Path.GetFileName(desiredPath)}.");
    }

    /// <summary>Renames the finished temp file to a free final name and returns that path.</summary>
    public string Commit()
    {
        if (_committed)
        {
            throw new InvalidOperationException("This output was already committed.");
        }

        if (!File.Exists(TempPath))
        {
            throw new FileNotFoundException("The job didn't write its output file.", TempPath);
        }

        for (int attempt = 0; attempt < MaxNameAttempts; attempt++)
        {
            string finalPath = GetFreePath(_desiredPath);
            try
            {
                File.Move(TempPath, finalPath, overwrite: false);
                _committed = true;
                return finalPath;
            }
            catch (IOException) when (IsTaken(finalPath))
            {
                // Another program took that name between our check and the rename; try the next one.
            }
        }

        throw new IOException($"Couldn't find a free name for {Path.GetFileName(_desiredPath)}.");
    }

    /// <summary>Deletes the temp file if the output was never committed (cancel or failure).</summary>
    public void Dispose()
    {
        if (_committed)
        {
            return;
        }

        try
        {
            File.Delete(TempPath); // does nothing if the file was never created
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually means the writer still has it open. File paths stay out of logs (CLAUDE.md 10.3).
            Log.Warning(ex, "Couldn't delete a partial output file");
        }
    }

    private static bool IsTaken(string path) => File.Exists(path) || Directory.Exists(path);
}