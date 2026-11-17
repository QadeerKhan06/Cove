// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>Helpers for <see cref="AlertPriority"/>.</summary>
public static class AlertPriorityExtensions
{
    /// <summary>
    /// True if an Alert with this priority still shows during quiet time (fullscreen, presentations,
    /// Do Not Disturb): job failures, finished timers and the mic going live.
    /// </summary>
    public static bool ShowsDuringQuietTime(this AlertPriority priority) => priority >= AlertPriority.JobFailed;
}
