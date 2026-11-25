using Cove.Core.Contracts;

namespace Cove.Core.Settings;

/// <summary>
/// The on/off switches from the Settings &gt; Modules page, saved through
/// <see cref="ISettingsStore"/> under <see cref="SectionId"/>.
/// </summary>
/// <remarks>
/// A module with no entry uses its own <see cref="IModule.EnabledByDefault"/>, so a new module
/// starts with its default the first time Cove runs with it.
/// </remarks>
public sealed class ModuleSwitches
{
    /// <summary>The settings section the switches are saved under.</summary>
    public const string SectionId = "modules";

    /// <summary>Module id → true for on, false for off.</summary>
    public Dictionary<string, bool> Enabled { get; set; } = new();
}
