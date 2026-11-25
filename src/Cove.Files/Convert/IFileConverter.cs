namespace Cove.Files.Convert;

/// <summary>
/// Converts one family of files, e.g. images. <see cref="ConvertJobRunner"/> picks the converter for
/// each input file, so a new family (video in F-09) only needs a new implementation of this.
/// </summary>
public interface IFileConverter
{
    /// <summary>The formats this converter can write, lowercase and without a dot, e.g. "jpg".</summary>
    IReadOnlyCollection<string> OutputFormats { get; }

    /// <summary>
    /// True if this converter can turn <paramref name="inputPath"/> into <paramref name="format"/>.
    /// Decides from the file extension only; it doesn't open the file.
    /// </summary>
    bool CanConvert(string inputPath, string format);

    /// <summary>
    /// Converts <paramref name="inputPath"/> and writes the result to <paramref name="outputPath"/>, which
    /// must not exist yet. Reports progress from 0 to 100. Throws if the file can't be converted; the
    /// caller deletes whatever was partly written.
    /// </summary>
    Task ConvertAsync(
        string inputPath,
        string outputPath,
        ConvertOptions options,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}
