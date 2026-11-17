// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// How important an event is if it becomes an Alert. When several alerts are waiting, the highest
/// priority shows first. During quiet time, only priorities where
/// <see cref="AlertPriorityExtensions.ShowsDuringQuietTime"/> is true are shown.
/// The order follows the spec's "Automatic pop-ups" table. Gaps leave room for new values.
/// </summary>
public enum AlertPriority
{
    /// <summary>Files copied in Explorer (off by default).</summary>
    FilesCopied = 10,

    /// <summary>Mic muted or unmuted from the shortcut.</summary>
    MicMuteChanged = 20,

    /// <summary>The song changed.</summary>
    SongChanged = 30,

    /// <summary>Auto-Sort moved a file (version 1.1).</summary>
    AutoSortMoved = 40,

    /// <summary>A screenshot was saved.</summary>
    ScreenshotSaved = 50,

    /// <summary>A download finished.</summary>
    DownloadFinished = 60,

    /// <summary>A device connected or disconnected (version 1.1).</summary>
    DeviceChanged = 70,

    /// <summary>A device's battery is low (version 1.1).</summary>
    LowBattery = 80,

    /// <summary>A job finished successfully.</summary>
    JobCompleted = 90,

    /// <summary>A job failed. This and everything above it still shows during quiet time.</summary>
    JobFailed = 100,

    /// <summary>A timer finished.</summary>
    TimerFinished = 110,

    /// <summary>An app started using the microphone.</summary>
    MicInUse = 120,
}
