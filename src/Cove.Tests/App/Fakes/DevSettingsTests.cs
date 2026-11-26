using Cove.App.Fakes;

namespace Cove.Tests.App.Fakes;

public sealed class DevSettingsTests
{
    [Fact]
    public void FakesAreOffByDefault()
    {
        Assert.False(new DevSettings().UseFakes);
    }

    [Theory]
    [InlineData(true, true, true)]    // Debug build, setting on → fakes run
    [InlineData(true, false, false)]  // Debug build, setting off → no fakes
    [InlineData(false, true, false)]  // Release build ignores the setting
    [InlineData(false, false, false)]
    public void FakesRunOnlyInDebugWithTheSettingOn(bool isDebugBuild, bool useFakes, bool expected)
    {
        var settings = new DevSettings { UseFakes = useFakes };

        Assert.Equal(expected, DevSettings.FakesAreOn(settings, isDebugBuild));
    }
}
