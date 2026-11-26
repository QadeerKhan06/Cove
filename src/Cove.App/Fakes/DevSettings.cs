using Cove.Core.Contracts;

namespace Cove.App.Fakes;

/// <summary>
/// Developer-only settings, saved through <see cref="ISettingsStore"/> under <see cref="SectionId"/>.
/// </summary>
/// <remarks>
/// Turning on <see cref="UseFakes"/> makes Cove use the fakes in this folder instead of real modules,
/// so the pill and panels can be built before the real modules exist (card K-11).
/// It only works in Debug builds: in a Release build the fakes can never switch on.
/// </remarks>
/// <example>
/// In <c>%AppData%\Cove\settings.json</c>:
/// <code>
/// "dev": { "useFakes": true }
/// </code>
/// </example>
public sealed class DevSettings
{
    /// <summary>The settings section these are saved under.</summary>
    public const string SectionId = "dev";

    /// <summary>True when this is a Debug build, the only kind where fakes may run.</summary>
#if DEBUG
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

    /// <summary>Use the fakes instead of real modules. Off unless a developer turns it on.</summary>
    public bool UseFakes { get; set; }

    /// <summary>Reads the saved setting and returns true if the fakes should run in this build.</summary>
    public static bool FakesAreOn(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return FakesAreOn(store.Get<DevSettings>(SectionId), IsDebugBuild);
    }

    /// <summary>
    /// The rule on its own: fakes run only when the setting is on <b>and</b> this is a Debug build.
    /// Separate so tests can check the Release case from a Debug test run.
    /// </summary>
    public static bool FakesAreOn(DevSettings settings, bool isDebugBuild)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return isDebugBuild && settings.UseFakes;
    }
}
