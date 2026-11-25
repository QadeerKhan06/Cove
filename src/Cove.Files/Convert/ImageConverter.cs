using System.IO;
using ImageMagick;

namespace Cove.Files.Convert;

/// <summary>Converts images with Magick.NET (ImageMagick).</summary>
public sealed class ImageConverter : IFileConverter
{
    // The ICO format can't hold an icon larger than this, in pixels.
    private const int MaxIconSize = 256;

    // EXIF standard, Orientation tag: 1 means the pixels are already upright.
    private const ushort UprightOrientation = 1;

    // Decoding is roughly half the work; encoding is the rest.
    private const double PercentAfterReading = 50;

    // The format is always given to ImageMagick from this list. If it guessed from the content, a
    // disguised file could reach one of its riskier readers (scripts, vector formats).
    private static readonly Dictionary<string, MagickFormat> InputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = MagickFormat.Png,
        [".jpg"] = MagickFormat.Jpeg,
        [".jpeg"] = MagickFormat.Jpeg,
        [".webp"] = MagickFormat.WebP,
        [".heic"] = MagickFormat.Heic,
        [".bmp"] = MagickFormat.Bmp,
        [".gif"] = MagickFormat.Gif,
        [".tif"] = MagickFormat.Tiff,
        [".tiff"] = MagickFormat.Tiff,
    };

    private static readonly Dictionary<string, MagickFormat> WritableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = MagickFormat.Png,
        ["jpg"] = MagickFormat.Jpeg,
        ["webp"] = MagickFormat.WebP,
        ["ico"] = MagickFormat.Ico,
        ["pdf"] = MagickFormat.Pdf,
    };

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutputFormats => WritableFormats.Keys;

    /// <inheritdoc />
    public bool CanConvert(string inputPath, string format) =>
        InputFormats.ContainsKey(Path.GetExtension(inputPath)) && WritableFormats.ContainsKey(format);

    /// <inheritdoc />
    public async Task ConvertAsync(
        string inputPath,
        string outputPath,
        ConvertOptions options,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (!CanConvert(inputPath, options.Format))
        {
            throw new NotSupportedException($"Can't convert this image to {options.Format}.");
        }

        MagickFormat outputFormat = WritableFormats[options.Format];
        using var image = new MagickImage();

        // ImageMagick calls this often while it works, so Cancel doesn't have to wait for a big image.
        image.Progress += (_, e) => e.Cancel = cancellationToken.IsCancellationRequested;

        await ReadAsync(image, inputPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(PercentAfterReading);

        MakeUpright(image);
        ShrinkToFit(image, options, outputFormat);
        PrepareForFormat(image, options, outputFormat);
        cancellationToken.ThrowIfCancellationRequested();

        // Streams, not paths: ImageMagick gives characters such as [1] and % in a path a special meaning.
        await using (FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write))
        {
            await image.WriteAsync(output, outputFormat, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(100);
    }

    private static async Task ReadAsync(MagickImage image, string inputPath, CancellationToken cancellationToken)
    {
        MagickFormat format = InputFormats[Path.GetExtension(inputPath)];
        var settings = new MagickReadSettings { Format = format };
        if (format is MagickFormat.Gif or MagickFormat.Tiff)
        {
            // Only the first frame or page is converted (decided in F-03), so don't decode the others.
            settings.FrameIndex = 0;
            settings.FrameCount = 1;
        }

        await using FileStream input = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await image.ReadAsync(input, settings, cancellationToken);
    }

    // Phones often store a photo sideways plus an EXIF note saying how to turn it. Not every program
    // reads the note, so this turns the pixels and then resets the note.
    private static void MakeUpright(MagickImage image)
    {
        image.AutoOrient();

        // If the note stayed behind, a viewer would turn the photo a second time.
        if (image.GetExifProfile() is { } exif
            && exif.GetValue(ExifTag.Orientation) is { } orientation
            && orientation.Value != UprightOrientation)
        {
            exif.SetValue(ExifTag.Orientation, UprightOrientation);
            image.SetProfile(exif);
        }
    }

    private static void ShrinkToFit(MagickImage image, ConvertOptions options, MagickFormat outputFormat)
    {
        int maxWidth = options.MaxWidth ?? int.MaxValue;
        int maxHeight = options.MaxHeight ?? int.MaxValue;
        if (outputFormat == MagickFormat.Ico)
        {
            maxWidth = Math.Min(maxWidth, MaxIconSize);
            maxHeight = Math.Min(maxHeight, MaxIconSize);
        }

        double scale = Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height);
        if (scale >= 1)
        {
            return; // already fits; never enlarge
        }

        // The size below already has the right proportions; don't let ImageMagick round it again.
        image.Resize(new MagickGeometry(Scaled(image.Width, scale), Scaled(image.Height, scale))
        {
            IgnoreAspectRatio = true,
        });
    }

    // At least 1, so a very long, thin image doesn't shrink to nothing.
    private static uint Scaled(uint length, double scale) => Math.Max(1, (uint)Math.Round(length * scale));

    private static void PrepareForFormat(MagickImage image, ConvertOptions options, MagickFormat outputFormat)
    {
        if (outputFormat is MagickFormat.Jpeg or MagickFormat.WebP)
        {
            image.Quality = (uint)options.Quality;
        }

        if (outputFormat == MagickFormat.Jpeg && image.HasAlpha)
        {
            // JPG has no transparency. Without this, see-through areas usually turn black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
    }
}
