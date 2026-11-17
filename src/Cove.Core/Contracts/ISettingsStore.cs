// DRAFT for K-08 review: needs approval from all three before merging.
namespace Cove.Core.Contracts;

/// <summary>
/// Saves and loads each module's settings. Everything lives in one JSON file
/// (<c>%AppData%\Cove\settings.json</c>), with one section per module id.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Settings classes are plain classes with a parameterless constructor and sensible defaults.</description></item>
/// <item><description>Missing sections or properties fall back to those defaults, so adding a new setting never breaks old files.</description></item>
/// <item><description>A damaged file is backed up and replaced with defaults; Cove never crashes on bad settings.</description></item>
/// <item><description>Saving publishes <see cref="CoveEventTypes.SettingsChanged"/> with the module id, so modules can react to edits made in the Settings window.</description></item>
/// <item><description>Writes to disk are delayed a few seconds and grouped, so saving often is cheap.</description></item>
/// <item><description>Never store secrets here. Keys and client ids belong in the git-ignored local config.</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TimerSettings
/// {
///     public int PomodoroWorkMinutes { get; set; } = 25;
///     public int PomodoroBreakMinutes { get; set; } = 5;
///     public bool PlaySound { get; set; } = true;
/// }
///
/// var settings = store.Get&lt;TimerSettings&gt;("timer");
/// settings.PlaySound = false;
/// store.Save("timer", settings);
/// </code>
/// </example>
public interface ISettingsStore
{
    /// <summary>
    /// Returns the saved settings for <paramref name="moduleId"/>, or a new <typeparamref name="T"/>
    /// with its default values if nothing has been saved yet. Returns a copy: changing it has no
    /// effect until you call <see cref="Save{T}"/>.
    /// </summary>
    T Get<T>(string moduleId) where T : class, new();

    /// <summary>Saves <paramref name="value"/> as the settings for <paramref name="moduleId"/>.</summary>
    void Save<T>(string moduleId, T value) where T : class, new();

    /// <summary>Replaces <paramref name="moduleId"/>'s settings with defaults (used by "Reset to defaults").</summary>
    void Reset(string moduleId);
}
