using Cove.Core.Contracts;
using Cove.UI.Pill;

namespace Cove.Tests.UI.Pill;

public sealed class AlertIconsTests
{
    // SPEC.md §7: download, success check, caution warning, timer, song.
    [Theory]
    [InlineData(CoveEventTypes.FileArrived, "", "TextFillColorPrimaryBrush")]
    [InlineData(JobEventTypes.Completed, "", "SystemFillColorSuccessBrush")]
    [InlineData(JobEventTypes.Failed, "", "SystemFillColorCautionBrush")]
    [InlineData(CoveEventTypes.ModuleFailed, "", "SystemFillColorCautionBrush")]
    [InlineData(CoveEventTypes.ShortcutConflict, "", "SystemFillColorCautionBrush")]
    [InlineData(CoveEventTypes.TimerFinished, "", "TextFillColorPrimaryBrush")]
    [InlineData(CoveEventTypes.SongChanged, "", "TextFillColorPrimaryBrush")]
    public void EachAlertType_GetsItsIcon(string eventType, string glyph, string brushKey)
    {
        Assert.Equal(new AlertIcon(glyph, brushKey), AlertIcons.For(eventType));
    }

    [Fact]
    public void MicAlert_IsTheDesignsFilledRedDot()
    {
        Assert.Equal(new AlertIcon(null, "SystemFillColorCriticalBrush"), AlertIcons.For(CoveEventTypes.MicStateChanged));
    }

    [Fact]
    public void UnknownType_GetsAnInfoIcon()
    {
        Assert.Equal(new AlertIcon("", "TextFillColorPrimaryBrush"), AlertIcons.For("timer.heads-up"));
    }
}
