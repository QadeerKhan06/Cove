using System.IO;
using Cove.Files.Convert;
using ImageMagick;

namespace Cove.Tests.Files.Convert;

public sealed class ImageConverterTests : IDisposable
{
    // EXIF standard, Orientation tag: 1 is upright, 6 means "turn a quarter turn clockwise to view".
    private const ushort UprightOrientation = 1;
    private const ushort QuarterTurnOrientation = 6;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CoveTests", Guid.NewGuid().ToString("N"));
    private readonly ImageConverter _converter = new();

    public ImageConverterTests() => Directory.CreateDirectory(_folder);

    // Runs after each test: removes this test's own temp folder.
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Every input and output format on the card except HEIC input, which needs a real photo.
    [Theory]
    [CombinatorialFormats]
    public async Task EveryFormat_ConvertsToEveryFormat(string inputExtension, string outputFormat)
    {
        string input = CreateImage("photo." + inputExtension, 40, 20);

        string output = await ConvertAsync(input, new ConvertOptions(outputFormat));

        AssertIsFormat(output, outputFormat);
    }

    [Fact]
    public void Heic_CanBeRead()
    {
        Assert.True(_converter.CanConvert("IMG_001.HEIC", "jpg"));
        Assert.Contains(MagickNET.SupportedFormats, f => f.Format == MagickFormat.Heic && f.SupportsReading);
    }

    [Theory]
    [InlineData("notes.txt", "jpg")]
    [InlineData("drawing.svg", "png")]
    [InlineData("photo.png", "mp4")]
    [InlineData("no-extension", "png")]
    public void OtherFormats_AreRefused(string input, string format) => Assert.False(_converter.CanConvert(input, format));

    [Fact]
    public async Task ASidewaysPhoto_ComesOutUpright()
    {
        // 40x20 pixels with a red left edge, plus the note "turn me a quarter turn clockwise". Upright,
        // it's 20x40 and the red edge is at the top.
        string input = Path.Combine(_folder, "phone.jpg");
        using (var photo = new MagickImage(MagickColors.Blue, 40, 20))
        using (var edge = new MagickImage(MagickColors.Red, 10, 20))
        {
            photo.Composite(edge, 0, 0, CompositeOperator.Over);
            File.WriteAllBytes(input, WithOrientationNote(photo.ToByteArray(MagickFormat.Jpeg), QuarterTurnOrientation));
        }

        // Make sure the test photo really is sideways, so a pass means the converter turned it.
        using (var check = new MagickImage(input))
        {
            Assert.Equal((40u, 20u), (check.Width, check.Height));
            Assert.Equal(QuarterTurnOrientation, check.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value);
        }

        string output = await ConvertAsync(input, new ConvertOptions("jpg"));

        using var result = new MagickImage(output);
        Assert.Equal((20u, 40u), (result.Width, result.Height));
        Assert.True(IsMostly(result, 10, 2, red: true), "the red edge should be at the top");
        Assert.True(IsMostly(result, 10, 37, red: false), "the bottom should be blue");
        ushort? note = result.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value;
        Assert.True(note is null or UprightOrientation, $"the note is still {note}");
    }

    [Theory]
    [InlineData(100, null, 100, 50)]
    [InlineData(null, 25, 50, 25)]
    [InlineData(100, 10, 20, 10)]
    [InlineData(1000, 1000, 200, 100)] // already fits: never enlarged
    public async Task AMaximumSize_ShrinksAndKeepsProportions(int? maxWidth, int? maxHeight, int width, int height)
    {
        string input = CreateImage("photo.png", 200, 100);

        string output = await ConvertAsync(input, new ConvertOptions("jpg", MaxWidth: maxWidth, MaxHeight: maxHeight));

        using var result = new MagickImage(output);
        Assert.Equal(((uint)width, (uint)height), (result.Width, result.Height));
    }

    [Fact]
    public async Task AnIcon_FitsIn256Pixels()
    {
        string input = CreateImage("photo.png", 800, 400);

        string output = await ConvertAsync(input, new ConvertOptions("ico"));

        using var result = new MagickImage(output, MagickFormat.Ico);
        Assert.Equal((256u, 128u), (result.Width, result.Height));
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    public async Task LowerQuality_GivesASmallerFile(string format)
    {
        string input = Path.Combine(_folder, "noise.png");
        using (var noise = new MagickImage("plasma:red-blue", 200, 200))
        {
            noise.Write(input);
        }

        string small = await ConvertAsync(input, new ConvertOptions(format, Quality: 20));
        string large = await ConvertAsync(input, new ConvertOptions(format, Quality: 95));

        Assert.True(new FileInfo(small).Length < new FileInfo(large).Length);
    }

    [Fact]
    public async Task TransparentAreas_BecomeWhiteInJpg()
    {
        string input = Path.Combine(_folder, "logo.png");
        using (var logo = new MagickImage(MagickColors.Transparent, 20, 20))
        {
            logo.Write(input);
        }

        string output = await ConvertAsync(input, new ConvertOptions("jpg"));

        using var result = new MagickImage(output);
        Assert.True(result.GetPixels().GetPixel(10, 10).ToColor()!.FuzzyEquals(MagickColors.White, new Percentage(2)));
    }

    [Fact]
    public async Task AnAnimatedGif_GivesOnlyItsFirstFrame()
    {
        string input = Path.Combine(_folder, "animation.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 20, 20));
            frames.Add(new MagickImage(MagickColors.Blue, 20, 20));
            frames.Write(input);
        }

        string output = await ConvertAsync(input, new ConvertOptions("png"));

        using var result = new MagickImageCollection(output);
        Assert.True(IsMostly(Assert.Single(result), 10, 10, red: true));
    }

    [Fact]
    public async Task PhotoMetadata_IsKept()
    {
        string input = Path.Combine(_folder, "dated.jpg");
        using (var photo = new MagickImage(MagickColors.Blue, 20, 20))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2026:11:25 10:00:00");
            photo.SetProfile(exif);
            photo.Write(input);
        }

        string output = await ConvertAsync(input, new ConvertOptions("webp"));

        using var result = new MagickImage(output);
        Assert.Equal("2026:11:25 10:00:00", result.GetExifProfile()?.GetValue(ExifTag.DateTimeOriginal)?.Value);
    }

    // ImageMagick would read "[1]" as a frame number and "%d" as a counter if it were given the path.
    [Theory]
    [InlineData("photo [1].png")]
    [InlineData("100%d done.png")]
    public async Task SpecialCharactersInTheName_Work(string name)
    {
        string input = CreateImage(name, 20, 20);

        string output = await ConvertAsync(input, new ConvertOptions("jpg"));

        AssertIsFormat(output, "jpg");
    }

    [Fact]
    public async Task TheOriginal_IsNotChanged()
    {
        string input = CreateImage("photo.png", 40, 20);
        byte[] before = File.ReadAllBytes(input);

        await ConvertAsync(input, new ConvertOptions("jpg", MaxWidth: 10));

        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task ADamagedFile_Throws()
    {
        string input = Path.Combine(_folder, "broken.png");
        File.WriteAllText(input, "this is not a picture");

        await Assert.ThrowsAnyAsync<MagickException>(() => ConvertAsync(input, new ConvertOptions("jpg")));
    }

    [Fact]
    public async Task ACancelledConvert_Throws()
    {
        string input = CreateImage("photo.png", 40, 20);
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ConvertAsync(input, new ConvertOptions("jpg"), cancelled));
    }

    private string CreateImage(string name, uint width, uint height)
    {
        string path = Path.Combine(_folder, name);
        using var image = new MagickImage(MagickColors.Blue, width, height);
        MagickFormat format = Enum.Parse<MagickFormat>(Path.GetExtension(name).TrimStart('.'), ignoreCase: true);
        using FileStream stream = File.Create(path);
        image.Write(stream, format);
        return path;
    }

    private async Task<string> ConvertAsync(
        string input, ConvertOptions options, CancellationToken cancellationToken = default)
    {
        string output = Path.Combine(_folder, $"{Guid.NewGuid():N}.{options.Format}");
        await _converter.ConvertAsync(input, output, options, new Progress<double>(), cancellationToken);
        return output;
    }

    // Adds an EXIF block holding only the Orientation tag, written by hand like a phone camera would,
    // so Magick.NET can't change the note while saving the test photo. Layout from the EXIF standard.
    private static byte[] WithOrientationNote(byte[] jpg, ushort orientation)
    {
        byte[] exif =
        [
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, // EXIF header
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,          // little-endian TIFF, first list at byte 8
            1, 0,                                             // the list has one entry
            0x12, 0x01, 3, 0, 1, 0, 0, 0,                     // tag 0x0112 Orientation, one 16-bit number
            (byte)orientation, 0, 0, 0,                       // its value
            0, 0, 0, 0,                                       // no further lists
        ];
        int length = exif.Length + 2; // a JPG segment length counts its own two bytes
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif]; // APP1 = EXIF

        // The segment goes straight after the JPG's first two bytes (its start marker).
        return [.. jpg[..2], .. segment, .. jpg[2..]];
    }

    // Checks the first bytes, so a file that only has the right extension doesn't pass.
    private static void AssertIsFormat(string path, string format)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] expected = format switch
        {
            "png" => [0x89, (byte)'P', (byte)'N', (byte)'G'],
            "jpg" => [0xFF, 0xD8, 0xFF],
            "webp" => "RIFF"u8.ToArray(),
            "ico" => [0, 0, 1, 0],
            "pdf" => "%PDF"u8.ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        Assert.Equal(expected, bytes.Take(expected.Length));
    }

    private static bool IsMostly(IMagickImage<ushort> image, int x, int y, bool red)
    {
        using var pixels = image.GetPixels();
        var color = pixels.GetPixel(x, y).ToColor()!;
        return red ? color.R > color.B : color.B > color.R;
    }

    private sealed class CombinatorialFormatsAttribute : Xunit.Sdk.DataAttribute
    {
        private static readonly string[] Inputs = ["png", "jpg", "jpeg", "webp", "bmp", "gif", "tif", "tiff"];
        private static readonly string[] Outputs = ["png", "jpg", "webp", "ico", "pdf"];

        public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod) =>
            from input in Inputs from output in Outputs select new object[] { input, output };
    }
}
