using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace Cove.Core.Settings;

/// <summary>
/// The settings file on disk: reading it, writing it without ever leaving half a file behind, and
/// backing it up before damaged data is replaced. Only <see cref="JsonSettingsStore"/> uses this,
/// and it does all the locking.
/// </summary>
/// <remarks>
/// Damaged data is never replaced without a backup. If a backup fails (for example because another
/// program has the file open), nothing is written; the backup is tried again before every later
/// write, and once it works, writing carries on as normal.
/// </remarks>
internal sealed class SettingsFile
{
    // People hand-edit this file, so reading forgives comments and trailing commas.
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly string _tempPath;
    private readonly TimeProvider _timeProvider;
    private BackupKind _pendingBackup = BackupKind.None;

    public SettingsFile(string path, TimeProvider timeProvider)
    {
        _path = Path.GetFullPath(path);
        _tempPath = _path + ".tmp";
        _timeProvider = timeProvider;
    }

    private enum BackupKind
    {
        None,

        // The whole file couldn't be read: move it out of the way, so a fresh one can replace it.
        Move,

        // One module's section was damaged: keep a copy, because the rest of the file is still in use.
        Copy,
    }

    /// <summary>
    /// Reads every module's section. No file yet means no sections: every module at its defaults.
    /// A file that can't be read is moved aside as a backup, and also gives no sections.
    /// </summary>
    public JsonObject Load()
    {
        if (!File.Exists(_path))
        {
            Log.Debug("No settings file at {Path} yet; using defaults", _path);
            return new JsonObject();
        }

        JsonObject? sections = TryRead();
        if (sections is not null)
        {
            return sections;
        }

        _pendingBackup = BackupKind.Move;
        TryPendingBackup();
        return new JsonObject();
    }

    /// <summary>
    /// Keeps a copy of the file as it is now, before a damaged section in it is replaced.
    /// If the copy fails, nothing is written until a later attempt succeeds.
    /// </summary>
    public void CopyToBackup()
    {
        // A move that's still waiting keeps the whole file anyway, so it covers this too.
        if (_pendingBackup == BackupKind.None)
        {
            _pendingBackup = BackupKind.Copy;
        }

        TryPendingBackup();
    }

    /// <summary>
    /// Writes every section to disk. Returns false if damaged data is still waiting to be backed up,
    /// or if Windows refused the write (for example the disk is full or the file is locked); the
    /// caller keeps its changes and tries again later.
    /// </summary>
    public bool TryWrite(JsonObject sections)
    {
        // Never replace damaged data that hasn't been backed up: try the backup again first.
        if (!TryPendingBackup())
        {
            return false;
        }

        try
        {
            string? folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Write a temporary file first, then rename it over the real one. On NTFS a rename
            // happens in one step, so if Cove crashes mid-save the real file is still the old
            // version, never half of the new one.
            File.WriteAllText(_tempPath, sections.ToJsonString(WriteOptions));
            File.Move(_tempPath, _path, overwrite: true);
            Log.Debug("Settings saved to {Path}", _path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows' error messages include the path, which only goes in Debug logs (CLAUDE.md 10.3).
            Log.Error("Couldn't save settings ({Error}); will try again on the next change", ex.GetType().Name);
            Log.Debug(ex, "Writing {Path} failed", _path);
            return false;
        }
    }

    // Returns the file's sections, or null if it can't be read or isn't a JSON object.
    private JsonObject? TryRead()
    {
        try
        {
            string json = File.ReadAllText(_path);
            if (JsonNode.Parse(json, documentOptions: ReadOptions) is JsonObject sections)
            {
                // JsonObject loads its properties lazily. Counting them loads them now, so a problem
                // such as the same name appearing twice shows up here instead of later, in Get.
                _ = sections.Count;
                return sections;
            }

            Log.Warning("The settings file isn't a JSON object; using defaults");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log.Warning("The settings file couldn't be read ({Error}); using defaults", ex.GetType().Name);
            Log.Debug(ex, "Reading {Path} failed", _path);
        }

        return null;
    }

    // Returns true when nothing is waiting to be backed up any more.
    private bool TryPendingBackup()
    {
        if (_pendingBackup == BackupKind.None)
        {
            return true;
        }

        if (!File.Exists(_path))
        {
            // The file is gone (for example the user deleted it), so there's nothing left to lose.
            _pendingBackup = BackupKind.None;
            return true;
        }

        try
        {
            string backupPath = ChooseBackupPath();
            if (_pendingBackup == BackupKind.Move)
            {
                File.Move(_path, backupPath);
            }
            else
            {
                File.Copy(_path, backupPath);
            }

            _pendingBackup = BackupKind.None;
            Log.Warning("Damaged settings backed up as {BackupName}", Path.GetFileName(backupPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Couldn't back up damaged settings ({Error}); nothing is saved until the backup works", ex.GetType().Name);
            Log.Debug(ex, "Backing up {Path} failed", _path);
            return false;
        }
    }

    // settings.json → settings.broken-20261118-143000.json, or "... (1).json", "... (2).json" if that
    // name is taken, so an earlier backup is never replaced (CLAUDE.md 10.6).
    private string ChooseBackupPath()
    {
        string folder = Path.GetDirectoryName(_path) ?? string.Empty;
        string stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string name = $"{Path.GetFileNameWithoutExtension(_path)}.broken-{stamp}";
        string extension = Path.GetExtension(_path);

        string candidate = Path.Combine(folder, name + extension);
        for (int number = 1; File.Exists(candidate); number++)
        {
            candidate = Path.Combine(folder, $"{name} ({number}){extension}");
        }

        return candidate;
    }
}
