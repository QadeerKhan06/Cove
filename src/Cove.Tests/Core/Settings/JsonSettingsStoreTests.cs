using System.IO;
using System.Text.Json.Nodes;
using Cove.Core.Contracts;
using Cove.Core.Settings;

namespace Cove.Tests.Core.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private const string TimerId = "timer";

    // Short so tests don't wait, but long enough that a "not written yet" check can't lose the race.
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(200);

    // Generous so a busy CI machine never fails a test; the write normally lands within the save delay.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    // Longer than any test, for tests where only Dispose should write.
    private static readonly TimeSpan NeverDuringTheTest = TimeSpan.FromMinutes(10);

    private readonly string _folder;
    private readonly string _filePath;
    private readonly FakeEventBus _bus = new();

    public JsonSettingsStoreTests()
    {
        // A fresh folder per test, under the test output (inside the repo and git-ignored).
        _folder = Path.Combine(AppContext.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _filePath = Path.Combine(_folder, "settings.json");
    }

    public void Dispose()
    {
        // Only this test's own files. The store never makes subfolders, so no recursive delete.
        foreach (string file in Directory.GetFiles(_folder))
        {
            File.Delete(file);
        }

        Directory.Delete(_folder);
    }

    [Fact]
    public void Get_WithNoFile_ReturnsDefaults()
    {
        using var store = CreateStore();

        var settings = store.Get<SampleSettings>(TimerId);

        Assert.Equal(25, settings.Minutes);
        Assert.True(settings.PlaySound);
    }

    [Fact]
    public void Get_AfterSave_ReturnsTheSavedValuesAsACopy()
    {
        using var store = CreateStore();
        store.Save(TimerId, new SampleSettings { Minutes = 10 });

        var first = store.Get<SampleSettings>(TimerId);
        first.Minutes = 99; // changing a copy must not change what's stored
        var second = store.Get<SampleSettings>(TimerId);

        Assert.Equal(10, second.Minutes);
    }

    [Fact]
    public void SavedSettings_SurviveARestart()
    {
        using (var store = CreateStore())
        {
            store.Save(TimerId, new SampleSettings { Minutes = 10, Mode = SampleMode.Loud });
        }

        using var restarted = CreateStore();
        var settings = restarted.Get<SampleSettings>(TimerId);

        Assert.Equal(10, settings.Minutes);
        Assert.Equal(SampleMode.Loud, settings.Mode);
    }

    [Fact]
    public void MissingProperty_GetsItsDefault_EvenInAHandEditedFile()
    {
        // Comments and trailing commas, as someone editing the file by hand might leave them.
        File.WriteAllText(_filePath, """
            {
              // edited by hand
              "timer": { "minutes": 10, },
            }
            """);
        using var store = CreateStore();

        var settings = store.Get<SampleSettings>(TimerId);

        Assert.Equal(10, settings.Minutes);
        Assert.True(settings.PlaySound);
    }

    [Fact]
    public void Save_KeepsOtherModulesSections()
    {
        // "media" stands in for a switched-off module: nothing reads its section this session.
        File.WriteAllText(_filePath, """{ "media": { "songAlerts": false }, "timer": { "minutes": 10 } }""");
        using (var store = CreateStore())
        {
            store.Save(TimerId, new SampleSettings { Minutes = 15 });
        }

        JsonNode? saved = JsonNode.Parse(File.ReadAllText(_filePath));

        Assert.Equal("""{"songAlerts":false}""", saved?["media"]?.ToJsonString());
        Assert.Equal(15, saved?[TimerId]?["minutes"]?.GetValue<int>());
    }

    [Fact]
    public void Save_WritesTheFileAfterTheDelay_NotStraightAway()
    {
        using var store = CreateStore();

        store.Save(TimerId, new SampleSettings { Minutes = 10 });
        store.Save(TimerId, new SampleSettings { Minutes = 11 });

        Assert.False(File.Exists(_filePath));
        Assert.True(SpinWait.SpinUntil(() => File.Exists(_filePath), WaitLimit));
        Assert.Equal(11, ReadMinutesFromDisk());
    }

    [Fact]
    public void Dispose_WritesWaitingChangesStraightAway()
    {
        var store = CreateStore(NeverDuringTheTest);
        store.Save(TimerId, new SampleSettings { Minutes = 10 });

        store.Dispose();

        Assert.Equal(10, ReadMinutesFromDisk());
    }

    [Fact]
    public void Reset_BringsBackTheDefaults_AndSavesThem()
    {
        using (var store = CreateStore())
        {
            store.Save(TimerId, new SampleSettings { Minutes = 10 });
            store.Reset(TimerId);

            Assert.Equal(25, store.Get<SampleSettings>(TimerId).Minutes);
        }

        using var restarted = CreateStore();
        Assert.Equal(25, restarted.Get<SampleSettings>(TimerId).Minutes);
    }

    [Fact]
    public void SaveAndReset_PublishSettingsChangedWithTheModuleId()
    {
        using var store = CreateStore();

        store.Save(TimerId, new SampleSettings());
        store.Reset(TimerId);

        Assert.Equal(2, _bus.Published.Count);
        Assert.All(_bus.Published, e =>
        {
            Assert.Equal(CoveEventTypes.SettingsChanged, e.Type);
            Assert.Equal(TimerId, e.GetData<string>());
            Assert.Null(e.Message); // never shown as an Alert
        });
    }

    [Theory]
    [InlineData("""{ "timer": { "minutes": 10 """)] // cut off part-way
    [InlineData("[]")] // valid JSON, but not an object
    [InlineData("null")]
    [InlineData("")] // empty file
    public void UnreadableFile_IsMovedToABackup_AndDefaultsAreUsed(string content)
    {
        File.WriteAllText(_filePath, content);
        byte[] original = File.ReadAllBytes(_filePath);

        using var store = CreateStore();

        Assert.Equal(25, store.Get<SampleSettings>(TimerId).Minutes);
        Assert.False(File.Exists(_filePath)); // moved aside; the next save writes a fresh one
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(BackupFiles())));
    }

    [Fact]
    public void DamagedSection_IsBackedUp_AndOnlyThatModuleIsReset()
    {
        const string content = """{ "media": { "songAlerts": false }, "timer": { "minutes": "ten" } }""";
        File.WriteAllText(_filePath, content);

        using (var store = CreateStore(NeverDuringTheTest))
        {
            Assert.Equal(25, store.Get<SampleSettings>(TimerId).Minutes);
        }

        // The backup is a copy of the file as it was; the live file now holds the reset.
        Assert.Equal(content, File.ReadAllText(Assert.Single(BackupFiles())));
        JsonNode? saved = JsonNode.Parse(File.ReadAllText(_filePath));
        Assert.Equal("""{"songAlerts":false}""", saved?["media"]?.ToJsonString());
        Assert.Null(saved?[TimerId]);
    }

    [Fact]
    public void NullSection_GetsDefaults_WithoutABackup()
    {
        File.WriteAllText(_filePath, """{ "timer": null }""");
        using var store = CreateStore();

        Assert.Equal(25, store.Get<SampleSettings>(TimerId).Minutes);
        Assert.Empty(BackupFiles());
    }

    [Fact]
    public void Backup_NeverReplacesAnEarlierBackup()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 14, 30, 0, TimeSpan.Zero));
        string earlierBackup = Path.Combine(_folder, "settings.broken-20261118-143000.json");
        File.WriteAllText(earlierBackup, "earlier");
        File.WriteAllText(_filePath, "not json");

        using var store = CreateStore(clock: clock);

        Assert.Equal("earlier", File.ReadAllText(earlierBackup));
        Assert.Equal("not json", File.ReadAllText(Path.Combine(_folder, "settings.broken-20261118-143000 (1).json")));
    }

    [Fact]
    public void FileThatCantBeBackedUp_IsNeverOverwritten()
    {
        File.WriteAllText(_filePath, """{ "timer": { "minutes": 10 } }""");
        byte[] original = File.ReadAllBytes(_filePath);

        // Stands in for another program holding the file open: Cove can't read it or move it.
        using (new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using var store = CreateStore(NeverDuringTheTest);
            Assert.Equal(25, store.Get<SampleSettings>(TimerId).Minutes); // defaults, no crash
            store.Save(TimerId, new SampleSettings { Minutes = 15 });

            // The store is disposed here, while the file is still locked, so its write is refused.
        }

        Assert.Equal(original, File.ReadAllBytes(_filePath));
        Assert.Empty(BackupFiles());
    }

    [Fact]
    public void BackupThatFailedAtStart_IsRetried_ThenSavingWorksNormally()
    {
        File.WriteAllText(_filePath, """{ "timer": { "minutes": 10 } }""");
        byte[] original = File.ReadAllBytes(_filePath);
        JsonSettingsStore store;

        using (new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store = CreateStore(NeverDuringTheTest); // can't read or back up the file yet
        }

        // The other program has let go. This save's write backs the file up first, then writes.
        store.Save(TimerId, new SampleSettings { Minutes = 15 });
        store.Dispose();

        Assert.Equal(original, File.ReadAllBytes(Assert.Single(BackupFiles())));
        Assert.Equal(15, ReadMinutesFromDisk());
    }

    private JsonSettingsStore CreateStore(TimeSpan? saveDelay = null, TimeProvider? clock = null) =>
        new(_filePath, _bus, saveDelay ?? SaveDelay, clock ?? TimeProvider.System);

    private int? ReadMinutesFromDisk() =>
        JsonNode.Parse(File.ReadAllText(_filePath))?[TimerId]?["minutes"]?.GetValue<int>();

    private string[] BackupFiles() => Directory.GetFiles(_folder, "settings.broken-*.json");

    public enum SampleMode
    {
        Quiet,
        Loud,
    }

    /// <summary>A settings class shaped like a real module's: plain properties with defaults.</summary>
    public sealed class SampleSettings
    {
        public int Minutes { get; set; } = 25;

        public bool PlaySound { get; set; } = true;

        public SampleMode Mode { get; set; } = SampleMode.Quiet;
    }

    /// <summary>Records published events. The store never subscribes, so subscribing isn't supported.</summary>
    private sealed class FakeEventBus : IEventBus
    {
        public List<CoveEvent> Published { get; } = new();

        public void Publish(CoveEvent coveEvent) => Published.Add(coveEvent);

        public IDisposable Subscribe(string eventType, Action<CoveEvent> handler) => throw new NotSupportedException();

        public IDisposable SubscribeAll(Action<CoveEvent> handler) => throw new NotSupportedException();
    }

    /// <summary>A clock stopped at one moment, in UTC, so backup names are predictable.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
