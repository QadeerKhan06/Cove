// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// How modules talk to each other and to the pill without referencing each other.
/// Publish what happened; subscribe to what you care about.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="Publish"/> can be called from any thread and returns immediately.</description></item>
/// <item><description>Handlers always run on the UI thread, in the order they subscribed. Keep them fast; start slow work in the background.</description></item>
/// <item><description>A handler that throws is logged and skipped; other handlers still run.</description></item>
/// <item><description>Dispose the returned subscription in <see cref="IModule.DisableAsync"/>.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // In a module's EnableAsync:
/// _subscription = _bus.Subscribe(CoveEventTypes.SongChanged, e =&gt;
/// {
///     var song = e.GetData&lt;SongInfo&gt;();
///     if (song is not null) _peek.SetSongTitle(song.Title);
/// });
///
/// // In DisableAsync:
/// _subscription?.Dispose();
/// </code>
/// </example>
public interface IEventBus
{
    /// <summary>Sends <paramref name="coveEvent"/> to every subscriber of its <see cref="CoveEvent.Type"/>.</summary>
    void Publish(CoveEvent coveEvent);

    /// <summary>
    /// Calls <paramref name="handler"/> for every event of type <paramref name="eventType"/>.
    /// Dispose the returned object to stop.
    /// </summary>
    IDisposable Subscribe(string eventType, Action<CoveEvent> handler);

    /// <summary>
    /// Calls <paramref name="handler"/> for every event of every type. Meant for the alert queue
    /// and logging only; modules should subscribe to specific types.
    /// </summary>
    IDisposable SubscribeAll(Action<CoveEvent> handler);
}
