using System.Globalization;

namespace Cove.Files.Convert;

/// <summary>The settings of a convert job, read from <see cref="Cove.Core.Contracts.Job.Options"/>.</summary>
/// <param name="Format">The format to convert to, lowercase and without a dot, e.g. "jpg".</param>
/// <param name="Quality">1 (smallest file) to 100 (best quality). Only used for JPG and WebP.</param>
/// <param name="MaxWidth">The widest the output may be, in pixels. Null means no limit.</param>
/// <param name="MaxHeight">The tallest the output may be, in pixels. Null means no limit.</param>
public sealed record ConvertOptions(
    string Format,
    int Quality = ConvertOptions.DefaultQuality,
    int? MaxWidth = null,
    int? MaxHeight = null)
{
    /// <summary>The option name for <see cref="Format"/>. Required.</summary>
    public const string FormatKey = "format";

    /// <summary>The option name for <see cref="Quality"/>.</summary>
    public const string QualityKey = "quality";

    /// <summary>The option name for <see cref="MaxWidth"/>.</summary>
    public const string MaxWidthKey = "maxWidth";

    /// <summary>The option name for <see cref="MaxHeight"/>.</summary>
    public const string MaxHeightKey = "maxHeight";

    /// <summary>The quality used when the job doesn't give one (decided in F-03).</summary>
    public const int DefaultQuality = 85;

    private const int MinQuality = 1;
    private const int MaxQuality = 100;

    /// <summary>Reads and checks a job's options.</summary>
    /// <exception cref="ArgumentException">The format is missing, or a number is invalid or out of range.</exception>
    public static ConvertOptions Parse(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.TryGetValue(FormatKey, out string? format) || string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException($"A convert job needs the '{FormatKey}' option.", nameof(options));
        }

        return new ConvertOptions(
            NormalizeFormat(format),
            ReadNumber(options, QualityKey, MinQuality, MaxQuality) ?? DefaultQuality,
            ReadNumber(options, MaxWidthKey, 1, int.MaxValue),
            ReadNumber(options, MaxHeightKey, 1, int.MaxValue));
    }

    // Accepts what people type in the command bar: "JPG", ".jpg" and "jpeg" all mean "jpg".
    private static string NormalizeFormat(string format)
    {
        string normalized = format.Trim().TrimStart('.').ToLowerInvariant();
        return normalized == "jpeg" ? "jpg" : normalized;
    }

    private static int? ReadNumber(IReadOnlyDictionary<string, string> options, string key, int min, int max)
    {
        if (!options.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Invariant culture: options are written by code, so they must not depend on the PC's language.
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            || number < min
            || number > max)
        {
            throw new ArgumentException($"'{text}' isn't a valid value for '{key}'.", nameof(options));
        }

        return number;
    }
}
