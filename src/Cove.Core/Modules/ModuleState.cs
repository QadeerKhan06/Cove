using Cove.Core.Contracts;

namespace Cove.Core.Modules;

/// <summary>Whether a module is running, as the <see cref="ModuleLoader"/> sees it.</summary>
public enum ModuleState
{
    /// <summary>Not running: switched off, not started yet, or stopped at Quit.</summary>
    Off,

    /// <summary>Running: <see cref="IModule.EnableAsync"/> finished without throwing.</summary>
    On,

    /// <summary>
    /// Threw while starting, so it was stopped again. Its saved switch stays on, so it's retried
    /// at the next launch, or when its switch goes off and back on.
    /// </summary>
    Failed,
}
