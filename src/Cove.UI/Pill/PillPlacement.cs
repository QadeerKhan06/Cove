namespace Cove.UI.Pill;

/// <summary>
/// Works out where the pill goes, in physical pixels, so it lands in the same spot at every
/// Windows scaling setting (100% to 200%).
/// </summary>
public static class PillPlacement
{
    /// <summary>
    /// Returns the top-left corner, in physical pixels, that centres a window on the monitor,
    /// <paramref name="topGap"/> DPI-independent pixels below its top edge.
    /// </summary>
    /// <param name="monitorLeft">The monitor's left edge, in physical pixels.</param>
    /// <param name="monitorTop">The monitor's top edge, in physical pixels.</param>
    /// <param name="monitorWidth">The monitor's width, in physical pixels.</param>
    /// <param name="scale">The monitor's scaling as a factor: 1.0 at 100%, 1.5 at 150%, 2.0 at 200%.</param>
    /// <param name="windowWidth">The window's width, in DPI-independent pixels.</param>
    /// <param name="topGap">The gap above the window, in DPI-independent pixels.</param>
    public static (int X, int Y) TopCenter(
        int monitorLeft, int monitorTop, int monitorWidth, double scale, double windowWidth, double topGap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var widthInPixels = ToPixels(windowWidth, scale);

        // Integer division: when a pixel is left over, it goes on the right, the same at every scale.
        var x = monitorLeft + ((monitorWidth - widthInPixels) / 2);
        var y = monitorTop + ToPixels(topGap, scale);
        return (x, y);
    }

    // Half a pixel rounds up, so the result never depends on the rounding mode's default.
    private static int ToPixels(double dips, double scale) =>
        (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
}
