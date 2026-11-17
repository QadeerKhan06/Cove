// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// Event type names used by more than one module. Each module may define its own extra types,
/// following the same "area.thing-happened" lowercase pattern, in its own <c>*EventTypes</c> class.
/// Adding a name here is a contract change (all three approve); adding one in your own module is not.
/// Job events (progress, completed, failed, cancelled) are not here: use <see cref="JobEventTypes"/>.
/// </summary>
/// <example>
/// <code>
/// bus.Subscribe(CoveEventTypes.QuietStarted, e =&gt; HoldAlerts());
/// </code>
/// </example>
public static class CoveEventTypes
{
    // Core (Lead)
    /// <summary>A module's settings were saved. <see cref="CoveEvent.Data"/> is the module id (string).</summary>
    public const string SettingsChanged = "core.settings-changed";

    /// <summary>A module failed and was disabled. <see cref="CoveEvent.Data"/> is the module id (string).</summary>
    public const string ModuleFailed = "core.module-failed";

    // System (System modules)
    /// <summary>A shortcut couldn't be registered because another app owns it.</summary>
    public const string ShortcutConflict = "system.shortcut-conflict";

    /// <summary>Fullscreen, presentation or Do Not Disturb started.</summary>
    public const string QuietStarted = "system.quiet-started";

    /// <summary>Quiet time ended.</summary>
    public const string QuietEnded = "system.quiet-ended";

    /// <summary>A finished download or saved screenshot arrived in a watched folder.</summary>
    public const string FileArrived = "system.file-arrived";

    /// <summary>The song changed.</summary>
    public const string SongChanged = "media.song-changed";

    /// <summary>The microphone started or stopped being used, or was muted or unmuted.</summary>
    public const string MicStateChanged = "system.mic-state-changed";

    /// <summary>A timer finished.</summary>
    public const string TimerFinished = "system.timer-finished";
}
