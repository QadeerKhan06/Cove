namespace Cove.UI.Pill;

/// <summary>
/// What the pill is showing. Sizes are in CLAUDE.md 9.5; Peek (C-13) and Menu (C-12) are added by
/// their own cards.
/// </summary>
public enum PillState
{
    /// <summary>Nothing on screen.</summary>
    Hidden,

    /// <summary>A one-line event, shown without taking focus.</summary>
    Alert,

    /// <summary>A module's panel, with keyboard focus until it closes.</summary>
    Panel,
}
