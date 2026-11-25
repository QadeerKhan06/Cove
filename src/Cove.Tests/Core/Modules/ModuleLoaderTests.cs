using Cove.Core.Contracts;
using Cove.Core.Modules;
using Cove.Core.Settings;

namespace Cove.Tests.Core.Modules;

public sealed class ModuleLoaderTests
{
    // Short so the "stuck module" test is quick, but far longer than a well-behaved fake needs.
    private static readonly TimeSpan StopTimeLimit = TimeSpan.FromMilliseconds(300);

    // Generous so a busy CI machine never fails a test.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly FakeSettingsStore _settings = new();
    private readonly FakeEventBus _bus = new();

    [Fact]
    public async Task Start_TurnsOnOnlyTheModulesSwitchedOn()
    {
        var files = new FakeModule("files");
        var media = new FakeModule("media");
        SaveSwitches(("files", true), ("media", false));
        var loader = CreateLoader(files, media);

        await loader.StartAsync();

        Assert.Equal(ModuleState.On, loader.GetState("files"));
        Assert.Equal(ModuleState.Off, loader.GetState("media"));
        Assert.Equal(0, media.EnableCalls);
    }

    [Fact]
    public async Task Start_WithNoSavedSwitch_UsesEachModulesDefault()
    {
        var files = new FakeModule("files") { EnabledByDefault = true };
        var voice = new FakeModule("voice") { EnabledByDefault = false };
        var loader = CreateLoader(files, voice);

        await loader.StartAsync();

        Assert.Equal(ModuleState.On, loader.GetState("files"));
        Assert.Equal(ModuleState.Off, loader.GetState("voice"));
    }

    [Fact]
    public async Task ModuleThatThrowsWhileStarting_IsFailedCleanedUpAndReported_AndTheRestRun()
    {
        var media = new FakeModule("media") { DisplayName = "Media", ThrowOnEnable = true };
        var files = new FakeModule("files");
        var loader = CreateLoader(media, files);

        await loader.StartAsync();

        Assert.Equal(ModuleState.Failed, loader.GetState("media"));
        Assert.Equal(1, media.DisableCalls); // given the chance to release what it half-started
        Assert.Equal(ModuleState.On, loader.GetState("files"));

        var failed = Assert.Single(_bus.Published);
        Assert.Equal(CoveEventTypes.ModuleFailed, failed.Type);
        Assert.Equal("media", failed.GetData<string>());
        Assert.Equal("Media stopped working and was switched off for now.", failed.Message);
    }

    [Fact]
    public async Task Stop_TurnsOffEveryRunningModule_EvenIfOneThrows()
    {
        var files = new FakeModule("files") { ThrowOnDisable = true };
        var media = new FakeModule("media");
        var voice = new FakeModule("voice") { EnabledByDefault = false };
        var loader = CreateLoader(files, media, voice);
        await loader.StartAsync();

        await loader.StopAsync();

        Assert.Equal(ModuleState.Off, loader.GetState("files"));
        Assert.Equal(ModuleState.Off, loader.GetState("media"));
        Assert.Equal(1, media.DisableCalls);
        Assert.Equal(0, voice.DisableCalls); // never started, so there's nothing to stop
    }

    [Fact]
    public async Task Stop_GivesUpOnAModuleThatHangs_AndStillStopsTheOthers()
    {
        var media = new FakeModule("media") { HangOnDisable = true };
        var files = new FakeModule("files");
        var loader = CreateLoader(media, files);
        await loader.StartAsync();

        // Throws TimeoutException, failing the test, if Stop waits for the stuck module forever.
        await loader.StopAsync().WaitAsync(WaitLimit);

        Assert.Equal(ModuleState.Off, loader.GetState("media"));
        Assert.Equal(ModuleState.Off, loader.GetState("files"));
        Assert.Equal(1, files.DisableCalls);
    }

    [Fact]
    public async Task DuplicateId_IsSkipped_AndTheFirstModuleStillWorks()
    {
        var first = new FakeModule("files");
        var second = new FakeModule("files");
        var loader = CreateLoader(first, second);

        await loader.StartAsync();

        Assert.Same(first, Assert.Single(loader.Modules));
        Assert.Equal(1, first.EnableCalls);
        Assert.Equal(0, second.EnableCalls);
    }

    [Fact]
    public async Task FlippingASwitch_TurnsOnlyThatModuleOffAndOn()
    {
        var files = new FakeModule("files");
        var media = new FakeModule("media");
        var loader = CreateLoader(files, media);
        await loader.StartAsync();

        FlipSwitch("media", on: false);
        WaitFor(() => loader.GetState("media") == ModuleState.Off);
        Assert.Equal(1, media.DisableCalls);

        FlipSwitch("media", on: true);
        WaitFor(() => loader.GetState("media") == ModuleState.On);
        Assert.Equal(2, media.EnableCalls);

        // Files was never touched.
        Assert.Equal(ModuleState.On, loader.GetState("files"));
        Assert.Equal(1, files.EnableCalls);
        Assert.Equal(0, files.DisableCalls);
    }

    [Fact]
    public async Task FailedModule_IsRetriedOnlyWhenItsSwitchGoesOffAndBackOn()
    {
        var media = new FakeModule("media") { ThrowOnEnable = true };
        var files = new FakeModule("files");
        var loader = CreateLoader(media, files);
        await loader.StartAsync();

        // Saving the switches for another reason must not retry media over and over.
        FlipSwitch("files", on: false);
        WaitFor(() => loader.GetState("files") == ModuleState.Off);
        Assert.Equal(ModuleState.Failed, loader.GetState("media"));
        Assert.Equal(1, media.EnableCalls);

        // Off and back on is the user asking for another try. This time the module starts fine.
        media.ThrowOnEnable = false;
        FlipSwitch("media", on: false);
        WaitFor(() => loader.GetState("media") == ModuleState.Off);
        FlipSwitch("media", on: true);
        WaitFor(() => loader.GetState("media") == ModuleState.On);

        Assert.Equal(2, media.EnableCalls);
        Assert.Equal(1, media.DisableCalls); // only the clean-up after it failed, never twice
    }

    [Fact]
    public async Task SettingsChangedForAnotherModule_ChangesNothing()
    {
        var files = new FakeModule("files");
        var loader = CreateLoader(files);
        await loader.StartAsync();
        SaveSwitches(("files", false)); // saved, but not announced yet

        AnnounceSettingsChanged("timer");

        // The loader ignores it straight away, inside Publish, so this can be checked at once.
        Assert.Equal(ModuleState.On, loader.GetState("files"));
        Assert.Equal(0, files.DisableCalls);

        // The same saved switches, announced for the modules section, do take effect.
        AnnounceSettingsChanged(ModuleSwitches.SectionId);
        WaitFor(() => loader.GetState("files") == ModuleState.Off);
    }

    [Fact]
    public async Task AfterStop_FlippingASwitchDoesNothing()
    {
        var media = new FakeModule("media") { EnabledByDefault = false };
        var loader = CreateLoader(media);
        await loader.StartAsync();
        await loader.StopAsync();

        FlipSwitch("media", on: true);

        // Stop unsubscribed, so the bus has no one to tell.
        Assert.Equal(ModuleState.Off, loader.GetState("media"));
        Assert.Equal(0, media.EnableCalls);
    }

    private static void WaitFor(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, WaitLimit));

    private ModuleLoader CreateLoader(params IModule[] modules) => new(modules, _settings, _bus, StopTimeLimit);

    private void SaveSwitches(params (string Id, bool On)[] switches)
    {
        var saved = new ModuleSwitches();
        foreach (var (id, on) in switches)
        {
            saved.Enabled[id] = on;
        }

        _settings.Save(ModuleSwitches.SectionId, saved);
    }

    // What the Settings > Modules page will do: save one switch, and the store announces it.
    private void FlipSwitch(string id, bool on)
    {
        var switches = _settings.Get<ModuleSwitches>(ModuleSwitches.SectionId);
        switches.Enabled[id] = on;
        _settings.Save(ModuleSwitches.SectionId, switches);
        AnnounceSettingsChanged(ModuleSwitches.SectionId);
    }

    // The real JsonSettingsStore publishes this whenever a section is saved.
    private void AnnounceSettingsChanged(string sectionId) =>
        _bus.Publish(new CoveEvent(CoveEventTypes.SettingsChanged, "core", AlertPriority.FilesCopied, Data: sectionId));

    /// <summary>A module that records calls and can be told to throw or hang.</summary>
    private sealed class FakeModule(string id) : IModule
    {
        public string Id { get; } = id;

        public string DisplayName { get; init; } = id;

        public string Description => "A module for tests.";

        public bool EnabledByDefault { get; init; } = true;

        // Settable, so a test can "fix" a module and check that it's retried.
        public bool ThrowOnEnable { get; set; }

        public bool ThrowOnDisable { get; init; }

        public bool HangOnDisable { get; init; }

        public int EnableCalls { get; private set; }

        public int DisableCalls { get; private set; }

        public Task EnableAsync(CancellationToken cancellationToken)
        {
            EnableCalls++;
            return ThrowOnEnable
                ? Task.FromException(new InvalidOperationException("Enable failed"))
                : Task.CompletedTask;
        }

        public Task DisableAsync()
        {
            DisableCalls++;
            if (ThrowOnDisable)
            {
                // Thrown straight away rather than from a task: the rudest way a module can fail.
                throw new InvalidOperationException("Disable failed");
            }

            // A task that never finishes stands in for a module that's stuck.
            return HangOnDisable ? new TaskCompletionSource().Task : Task.CompletedTask;
        }
    }

    /// <summary>Keeps settings in memory instead of a file.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object> _sections = new();

        public T Get<T>(string moduleId) where T : class, new() =>
            _sections.TryGetValue(moduleId, out var value) ? (T)value : new T();

        public void Save<T>(string moduleId, T value) where T : class, new() => _sections[moduleId] = value;

        public void Reset(string moduleId) => _sections.Remove(moduleId);
    }

    /// <summary>
    /// Records published events and calls subscribers straight away, on the calling thread.
    /// (The real bus calls them on the UI thread.)
    /// </summary>
    private sealed class FakeEventBus : IEventBus
    {
        private readonly object _lock = new();
        private readonly List<(string Type, Action<CoveEvent> Handler)> _handlers = new();
        private readonly List<CoveEvent> _published = new();

        public IReadOnlyList<CoveEvent> Published
        {
            get
            {
                lock (_lock)
                {
                    return _published.ToList();
                }
            }
        }

        public void Publish(CoveEvent coveEvent)
        {
            List<Action<CoveEvent>> toCall;
            lock (_lock)
            {
                _published.Add(coveEvent);
                toCall = _handlers.Where(h => h.Type == coveEvent.Type).Select(h => h.Handler).ToList();
            }

            foreach (var handler in toCall)
            {
                handler(coveEvent);
            }
        }

        public IDisposable Subscribe(string eventType, Action<CoveEvent> handler)
        {
            var entry = (eventType, handler);
            lock (_lock)
            {
                _handlers.Add(entry);
            }

            return new Unsubscriber(() =>
            {
                lock (_lock)
                {
                    _handlers.Remove(entry);
                }
            });
        }

        public IDisposable SubscribeAll(Action<CoveEvent> handler) => throw new NotSupportedException();

        private sealed class Unsubscriber(Action unsubscribe) : IDisposable
        {
            public void Dispose() => unsubscribe();
        }
    }
}
