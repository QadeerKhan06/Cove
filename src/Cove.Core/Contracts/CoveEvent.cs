// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// Something that happened in Cove, published on the <see cref="IEventBus"/>.
/// Any module can publish events; the alert queue decides whether an event becomes an Alert,
/// only updates Peek, or stays silent (based on <see cref="Message"/>, <see cref="Priority"/>,
/// quiet time, and the user's per-event settings).
/// </summary>
/// <param name="Type">
/// What happened, as "area.thing-happened" in lowercase, e.g. "media.song-changed",
/// "system.file-arrived". See <see cref="CoveEventTypes"/> for the shared ones and
/// <see cref="JobEventTypes"/> for job events.
/// </param>
/// <param name="Source">The <see cref="IModule.Id"/> of the module that published it.</param>
/// <param name="Priority">How important it is if it becomes an Alert. Higher wins.</param>
/// <param name="Message">
/// The one-line text to show if this event becomes an Alert, e.g. "Saved Essay.pdf".
/// Null means this event is never shown as an Alert (it may still update Peek or other modules).
/// </param>
/// <param name="Data">
/// Optional details for subscribers, usually a small record defined by the publishing module,
/// e.g. <c>new SongInfo("Title", "Artist")</c>. Read it with <see cref="GetData{T}"/>.
/// Never put file contents, tokens or recognized speech here.
/// </param>
/// <example>
/// <code>
/// bus.Publish(new CoveEvent(
///     Type: JobEventTypes.Completed,
///     Source: "files",
///     Priority: AlertPriority.JobCompleted,
///     Message: "Saved photo.jpg",
///     Data: new JobResult(jobId, true, [outputPath], null, null, null))
/// {
///     Actions = new[]
///     {
///         new CoveAction("Open", () =&gt; shell.OpenAsync(outputPath)),
///         new CoveAction("Show in folder", () =&gt; shell.RevealAsync(outputPath)),
///     },
/// });
/// </code>
/// </example>
public sealed record CoveEvent(
    string Type,
    string Source,
    AlertPriority Priority,
    string? Message = null,
    object? Data = null)
{
    /// <summary>Up to two buttons shown on the Alert. More than two is an error.</summary>
    public IReadOnlyList<CoveAction> Actions { get; init; } = Array.Empty<CoveAction>();

    /// <summary>When the event was created. Used for merging and logging.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>Unique id, for logging and de-duplication.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Returns <see cref="Data"/> as <typeparamref name="T"/>, or null if it's missing or another type.</summary>
    public T? GetData<T>() where T : class => Data as T;
}
