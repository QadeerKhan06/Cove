using Cove.UI.Pill;

namespace Cove.Tests.UI.Pill;

public sealed class PillPlacementTests
{
    // CLAUDE.md 9.5: the pill sits 8 DPI-independent pixels below the top edge.
    private const double TopGap = 8;

    // The pill's width in Peek, in DPI-independent pixels.
    private const double PillWidth = 200;

    [Theory]
    [InlineData(1920, 1.00, 860, 8)] // 100%: 200 px wide
    [InlineData(1920, 1.25, 835, 10)] // 125%: 250 px wide
    [InlineData(2560, 1.50, 1130, 12)] // 150%: 300 px wide
    [InlineData(3840, 1.75, 1745, 14)] // 175%: 350 px wide
    [InlineData(3840, 2.00, 1720, 16)] // 200%: 400 px wide
    public void TopCenter_CentresThePill_AndScalesTheGapAboveIt(int monitorWidth, double scale, int expectedX, int expectedY)
    {
        var (x, y) = PillPlacement.TopCenter(0, 0, monitorWidth, scale, PillWidth, TopGap);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void TopCenter_AtAFractionalWidth_RoundsToWholePixels()
    {
        // 201 x 1.25 = 251.25 px, so 251 px wide; 1920 - 251 = 1669 px left over, 834 on the left.
        var (x, _) = PillPlacement.TopCenter(0, 0, 1920, 1.25, 201, TopGap);

        Assert.Equal(834, x);
    }

    [Fact]
    public void TopCenter_AtExactlyHalfAPixel_RoundsUp()
    {
        // 202 x 1.25 = 252.5 px, rounded up to 253; 1920 - 253 = 1667 px left over, 833 on the left.
        var (x, _) = PillPlacement.TopCenter(0, 0, 1920, 1.25, 202, TopGap);

        Assert.Equal(833, x);
    }

    [Fact]
    public void TopCenter_OnAMonitorThatDoesNotStartAtZero_AddsItsEdges()
    {
        var (x, y) = PillPlacement.TopCenter(100, 50, 1920, 1.00, PillWidth, TopGap);

        Assert.Equal(960, x);
        Assert.Equal(58, y);
    }

    [Fact]
    public void TopCenter_WithAZeroScale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PillPlacement.TopCenter(0, 0, 1920, 0, PillWidth, TopGap));
    }
}
