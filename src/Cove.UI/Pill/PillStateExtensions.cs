namespace Cove.UI.Pill;

/// <summary>Rules that belong to each <see cref="PillState"/>.</summary>
public static class PillStateExtensions
{
    /// <summary>
    /// Whether the pill takes keyboard focus in this state (CLAUDE.md 9.5). The Menu and panels do;
    /// alerts, Peek and the progress pill never do, so typing in another app is never interrupted.
    /// </summary>
    /// <remarks>Every new state must be added here: an unlisted one throws, so tests catch it.</remarks>
    public static bool TakesFocus(this PillState state) => state switch
    {
        PillState.Hidden => false,
        PillState.Alert => false,
        PillState.Panel => true,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "This pill state doesn't say whether it takes focus."),
    };
}
