using Cove.Core.Contracts;

namespace Cove.UI.Pill;

/// <summary>
/// Which icon each kind of alert shows (SPEC.md §7). <see cref="CoveEvent"/> has no icon of its own,
/// so the pill picks one from the event type.
/// </summary>
public static class AlertIcons
{
    // Character codes from Microsoft's "Segoe Fluent Icons font" page (checked 2026-11-27).
    private const string Download = "";
    private const string CheckMark = "";
    private const string Warning = "";
    private const string Stopwatch = ""; // the font has no "Timer" icon; this is the closest
    private const string MusicNote = "";
    private const string Info = "";

    // Theme brushes from WPF-UI 4.3.0's theme files (checked 2026-11-27).
    private const string Text = "TextFillColorPrimaryBrush";
    private const string Success = "SystemFillColorSuccessBrush";
    private const string Caution = "SystemFillColorCautionBrush";
    private const string Critical = "SystemFillColorCriticalBrush";

    /// <summary>The icon for an alert of this event type; an info icon for any type not listed.</summary>
    public static AlertIcon For(string eventType) => eventType switch
    {
        CoveEventTypes.FileArrived => new(Download, Text),
        JobEventTypes.Completed => new(CheckMark, Success),
        JobEventTypes.Failed or CoveEventTypes.ModuleFailed or CoveEventTypes.ShortcutConflict => new(Warning, Caution),
        CoveEventTypes.TimerFinished => new(Stopwatch, Text),
        CoveEventTypes.SongChanged => new(MusicNote, Text),

        // The design's filled red dot. S-13 adds the hollow, slashed circle for "muted".
        CoveEventTypes.MicStateChanged => new(null, Critical),
        _ => new(Info, Text),
    };
}
