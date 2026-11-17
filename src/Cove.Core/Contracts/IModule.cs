// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// A feature of Cove that can be switched on and off on its own, such as Files, Media or Timer.
/// The module loader enables every module whose saved setting is "on" at startup, and enables or
/// disables modules when the user flips their switch on the Settings > Modules page.
/// </summary>
/// <remarks>
/// <para>
/// When a module is enabled, the loader registers its <see cref="Panels"/> with the pill and its
/// <see cref="Commands"/> with the command router. When it is disabled, the loader removes them again,
/// so a switched-off module leaves nothing behind in the Menu, the command bar or voice.
/// </para>
/// <para>
/// A module that throws from <see cref="EnableAsync"/> or <see cref="DisableAsync"/> is disabled and
/// logged; it never takes the rest of Cove down with it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TimerModule : IModule
/// {
///     private readonly IEventBus _bus;
///     public TimerModule(IEventBus bus) => _bus = bus;
///
///     public string Id => "timer";
///     public string DisplayName => "Timer and Focus";
///     public string Description => "Quick timers and Pomodoro, with a countdown in the pill.";
///
///     public IReadOnlyList&lt;IPanel&gt; Panels => new[] { new TimerPanel() };
///     public IReadOnlyList&lt;ICoveCommand&gt; Commands => new ICoveCommand[] { new TimerCommand(this) };
///
///     public Task EnableAsync(CancellationToken ct) => Task.CompletedTask;   // start nothing until asked
///     public Task DisableAsync() { StopAllTimers(); return Task.CompletedTask; }
/// }
/// </code>
/// </example>
public interface IModule
{
    /// <summary>
    /// Stable, lowercase id used in settings, logs and events, e.g. "files", "media", "timer".
    /// Never change it after release: saved settings are stored under this id.
    /// </summary>
    string Id { get; }

    /// <summary>Name shown to users, e.g. "Timer and Focus".</summary>
    string DisplayName { get; }

    /// <summary>One sentence shown on the module's card in Settings > Modules.</summary>
    string Description { get; }

    /// <summary>Whether the module is on the first time Cove runs. Defaults to true.</summary>
    bool EnabledByDefault => true;

    /// <summary>
    /// The settings class this module saves through <see cref="ISettingsStore"/>, or null if it has none.
    /// Lets Settings offer "Reset to defaults" for the module.
    /// </summary>
    Type? SettingsType => null;

    /// <summary>Panels this module adds to the pill (shown in the Cove key Menu). Empty if none.</summary>
    IReadOnlyList<IPanel> Panels => Array.Empty<IPanel>();

    /// <summary>Commands this module adds to the command bar and voice. Empty if none.</summary>
    IReadOnlyList<ICoveCommand> Commands => Array.Empty<ICoveCommand>();

    /// <summary>
    /// Start the module: subscribe to events, start watchers, register hooks.
    /// Must not block; do slow setup asynchronously. Respect <paramref name="cancellationToken"/>.
    /// </summary>
    Task EnableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the module and release everything it holds: event subscriptions, file watchers,
    /// keyboard hooks, audio devices, the microphone. After this returns, the module must use
    /// no CPU and hold no system resources.
    /// </summary>
    Task DisableAsync();
}
