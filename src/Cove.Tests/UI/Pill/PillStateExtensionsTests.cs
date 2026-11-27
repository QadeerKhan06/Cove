using Cove.UI.Pill;

namespace Cove.Tests.UI.Pill;

public sealed class PillStateExtensionsTests
{
    // CLAUDE.md 9.5: the Menu and panels take focus; alerts, Peek and the progress pill never do.
    [Theory]
    [InlineData(PillState.Hidden, false)]
    [InlineData(PillState.Alert, false)]
    [InlineData(PillState.Panel, true)]
    public void TakesFocus_MatchesTheStatesTable(PillState state, bool takesFocus)
    {
        Assert.Equal(takesFocus, state.TakesFocus());
    }

    [Fact]
    public void EveryState_SaysWhetherItTakesFocus()
    {
        foreach (var state in Enum.GetValues<PillState>())
        {
            _ = state.TakesFocus(); // throws for a state that was added without a rule
        }
    }
}
