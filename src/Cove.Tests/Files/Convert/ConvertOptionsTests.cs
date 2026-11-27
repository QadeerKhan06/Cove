using Cove.Files.Convert;

namespace Cove.Tests.Files.Convert;

public sealed class ConvertOptionsTests
{
    [Fact]
    public void OnlyAFormat_UsesTheDefaults()
    {
        var options = Parse(("format", "png"));

        Assert.Equal(new ConvertOptions("png", ConvertOptions.DefaultQuality, null, null), options);
    }

    [Fact]
    public void EveryOption_IsRead()
    {
        var options = Parse(("format", "webp"), ("quality", "60"), ("maxWidth", "800"), ("maxHeight", "600"));

        Assert.Equal(new ConvertOptions("webp", 60, 800, 600), options);
    }

    [Theory]
    [InlineData("JPG")]
    [InlineData(".jpg")]
    [InlineData("jpeg")]
    [InlineData(" Jpeg ")]
    public void JpgCanBeSpelledSeveralWays(string format) => Assert.Equal("jpg", Parse(("format", format)).Format);

    [Fact]
    public void AMissingFormat_Throws() => Assert.Throws<ArgumentException>(() => Parse(("quality", "60")));

    [Theory]
    [InlineData("quality", "0")]
    [InlineData("quality", "101")]
    [InlineData("quality", "high")]
    [InlineData("maxWidth", "0")]
    [InlineData("maxHeight", "-5")]
    public void AnInvalidNumber_Throws(string key, string value) =>
        Assert.Throws<ArgumentException>(() => Parse(("format", "jpg"), (key, value)));

    private static ConvertOptions Parse(params (string Key, string Value)[] options) =>
        ConvertOptions.Parse(options.ToDictionary(o => o.Key, o => o.Value));
}
