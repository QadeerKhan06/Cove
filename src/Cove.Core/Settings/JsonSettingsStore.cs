using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cove.Core.Contracts;
using Serilog;

namespace Cove.Core.Settings;

/// <summary>
/// Cove's <see cref="ISettingsStore"/>: every module's settings in one JSON file, with one
/// section per module id.
/// </summary>
/// <remarks>
/// <para>
/// The file is read once, when the store is created. After that, reads come from memory, and the
/// file is written <see cref="DefaultSaveDelay"/> after the last change, so saving on every
/// keystroke costs nothing. Dispose the store when Cove quits, to write anything still waiting.
/// </para>
/// <para>
/// Damaged settings never crash Cove. If one module's section is damaged, the file is backed up
/// and only that module goes back to its defaults. If the whole file can't be read, it is moved
/// aside as a backup and every module starts from its defaults.
/// </para>
/// <para>Safe to use from any thread.</para>
/// </remarks>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    /// <summary>How long after the last change the file is written ("a few seconds", card C-02).</summary>
    public static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromSeconds(2);

    // The store belongs to Core, so the events it publishes come from "core".
    private const string EventSource = "core";

    // camelCase like the rest of the file; enums as text so people can read and edit them.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly SettingsFile _file;
    private readonly IEventBus _eventBus;
    private readonly TimeSpan _saveDelay;
    private readonly ITimer _saveTimer;
    private readonly JsonObject _sections;
    private bool _hasUnsavedChanges;
    private bool _disposed;

    /// <summary>Opens the settings file at <paramref name="filePath"/>, normally <see cref="DefaultFilePath"/>.</summary>
    public JsonSettingsStore(string filePath, IEventBus eventBus)
        : this(filePath, eventBus, DefaultSaveDelay, TimeProvider.System)
    {
    }

    /// <summary>
    /// Opens the settings file with a custom save delay and clock. Tests use this so they don't
    /// wait, and so backup names are predictable.
    /// </summary>
    public JsonSettingsStore(string filePath, IEventBus eventBus, TimeSpan saveDelay, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _file = new SettingsFile(filePath, timeProvider);
        _eventBus = eventBus;
        _saveDelay = saveDelay;
        _sections = _file.Load();

        // Created stopped. Each change restarts it (see ScheduleWrite).
        _saveTimer = timeProvider.CreateTimer(
            _ => OnSaveTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary><c>%AppData%\Cove\settings.json</c> for the current Windows user.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cove", "settings.json");

    /// <inheritdoc />
    public T Get<T>(string moduleId) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleId);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                // Built fresh from the JSON every time, so each caller gets its own copy.
                // Properties missing from the file keep the defaults set by T's constructor.
                return _sections[moduleId].Deserialize<T>(SerializerOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                ResetDamagedSection(moduleId, ex);
                return new T();
            }
        }
    }

    /// <inheritdoc />
    public void Save<T>(string moduleId, T value) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleId);
        ArgumentNullException.ThrowIfNull(value);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sections[moduleId] = JsonSerializer.SerializeToNode(value, SerializerOptions);
            ScheduleWrite();
        }

        // Outside the lock, so a handler that calls Get straight away never waits on us.
        PublishChanged(moduleId);
    }

    /// <inheritdoc />
    public void Reset(string moduleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleId);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // With no section, Get falls back to the settings class's own defaults.
            _sections.Remove(moduleId);
            ScheduleWrite();
        }

        PublishChanged(moduleId);
    }

    /// <summary>Writes any change still waiting for the save delay, then stops the store.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _saveTimer.Dispose();
            WritePendingChanges();
        }
    }

    // Caller holds _lock. Only this module's section is damaged (for example "yes" where a number
    // belongs), so back up the file, then give just this module its defaults. Other modules keep theirs.
    private void ResetDamagedSection(string moduleId, JsonException error)
    {
        Log.Warning("Settings for {ModuleId} were damaged; using its defaults", moduleId);
        Log.Debug(error, "Settings section {ModuleId} couldn't be read", moduleId);
        _file.CopyToBackup();
        _sections.Remove(moduleId);
        ScheduleWrite();
    }

    // Restarts the countdown, so a burst of changes leads to one write, after the last one.
    private void ScheduleWrite()
    {
        _hasUnsavedChanges = true;
        _saveTimer.Change(_saveDelay, Timeout.InfiniteTimeSpan);
    }

    // Runs on a thread-pool thread. An exception escaping from here would end the whole app,
    // so anything unexpected is logged instead.
    private void OnSaveTimer()
    {
        try
        {
            lock (_lock)
            {
                WritePendingChanges();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error while saving settings");
        }
    }

    // Caller holds _lock, which also stops two writes from overlapping. The file is a few KB,
    // so holding the lock while writing is quick.
    private void WritePendingChanges()
    {
        // If the write fails, _hasUnsavedChanges stays true, so the next change or Dispose tries again.
        if (_hasUnsavedChanges && _file.TryWrite(_sections))
        {
            _hasUnsavedChanges = false;
        }
    }

    private void PublishChanged(string moduleId) =>
        _eventBus.Publish(new CoveEvent(
            CoveEventTypes.SettingsChanged,
            EventSource,
            // Message is null, so this never becomes an Alert and the priority is never used.
            // The contract has no "none" priority yet, so this uses the lowest one.
            // TODO(K-08): switch to AlertPriority.None once the contract has it.
            AlertPriority.FilesCopied,
            Message: null,
            Data: moduleId));
}
