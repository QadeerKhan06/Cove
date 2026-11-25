using Cove.Core.Contracts;
using Cove.Core.Settings;
using Serilog;

namespace Cove.Core.Modules;

/// <summary>
/// Starts and stops Cove's modules. At startup it turns on every module whose switch on the
/// Settings &gt; Modules page is on, follows the switches while Cove runs, and at Quit turns them
/// all off.
/// </summary>
/// <remarks>
/// A module that throws while starting is stopped again, marked <see cref="ModuleState.Failed"/>,
/// logged and reported on the event bus; the other modules keep running.
/// </remarks>
public sealed class ModuleLoader
{
    /// <summary>
    /// How long each module gets to stop before Cove gives up on it, so one stuck module can't
    /// freeze Quit. Five seconds is long enough for any well-behaved module (card C-03 plan).
    /// </summary>
    public static readonly TimeSpan DefaultStopTimeLimit = TimeSpan.FromSeconds(5);

    // The loader belongs to Core, so the events it publishes come from "core".
    private const string EventSource = "core";

    private readonly ISettingsStore _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeSpan _stopTimeLimit;
    private readonly List<IModule> _modules = new();
    private readonly Dictionary<string, ModuleState> _states = new();
    private readonly object _stateLock = new();

    // Cancelled at Quit, so modules that are still starting are told to give up.
    private readonly CancellationTokenSource _lifetime = new();

    // Only one start, stop or switch change runs at a time, so a module is never being turned
    // on and off at once.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IDisposable? _switchSubscription;

    /// <summary>Registers <paramref name="modules"/>; nothing starts until <see cref="StartAsync"/>.</summary>
    public ModuleLoader(IEnumerable<IModule> modules, ISettingsStore settings, IEventBus eventBus)
        : this(modules, settings, eventBus, DefaultStopTimeLimit)
    {
    }

    /// <summary>Same as the other constructor, with a custom stop time limit. Tests use this so they don't wait.</summary>
    public ModuleLoader(IEnumerable<IModule> modules, ISettingsStore settings, IEventBus eventBus, TimeSpan stopTimeLimit)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(eventBus);

        _settings = settings;
        _eventBus = eventBus;
        _stopTimeLimit = stopTimeLimit;

        foreach (var module in modules)
        {
            Register(module);
        }
    }

    /// <summary>Every registered module, in the order they were given.</summary>
    public IReadOnlyList<IModule> Modules => _modules;

    /// <summary>Whether the module with <paramref name="moduleId"/> is off, on, or failed.</summary>
    public ModuleState GetState(string moduleId)
    {
        lock (_stateLock)
        {
            return _states.TryGetValue(moduleId, out var state)
                ? state
                : throw new ArgumentException($"No module with the id '{moduleId}' is registered.", nameof(moduleId));
        }
    }

    /// <summary>
    /// Turns on every module whose switch is on, then keeps following the switches until
    /// <see cref="StopAsync"/>. Call once, at startup.
    /// </summary>
    public async Task StartAsync()
    {
        // Subscribe first, so a switch flipped while modules are starting isn't missed:
        // the change waits at the gate until the start has finished.
        _switchSubscription ??= _eventBus.Subscribe(CoveEventTypes.SettingsChanged, OnSettingsChanged);

        await _gate.WaitAsync();
        try
        {
            var switches = _settings.Get<ModuleSwitches>(ModuleSwitches.SectionId);
            var toStart = _modules.Where(module => IsSwitchedOn(module, switches));

            // All at once, so one slow module doesn't hold up the others.
            await Task.WhenAll(toStart.Select(TurnOnAsync));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops following the switches and turns off every running module. Call once, when Cove quits.</summary>
    public async Task StopAsync()
    {
        _switchSubscription?.Dispose();
        _switchSubscription = null;

        // Tell modules that are still starting to give up, then wait for that to finish.
        _lifetime.Cancel();
        await _gate.WaitAsync();
        try
        {
            var running = _modules.Where(module => GetState(module.Id) == ModuleState.On).ToList();
            await Task.WhenAll(running.Select(TurnOffAsync));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSwitchedOn(IModule module, ModuleSwitches switches) =>
        switches.Enabled.TryGetValue(module.Id, out var on) ? on : module.EnabledByDefault;

    private void Register(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (_stateLock)
        {
            if (_states.ContainsKey(module.Id))
            {
                Log.Error("Two modules use the id {ModuleId}; the second one ({Type}) was skipped", module.Id, module.GetType().Name);
                return;
            }

            _states[module.Id] = ModuleState.Off;
        }

        _modules.Add(module);
    }

    // Runs on the UI thread (IEventBus contract), so it only checks the event and hands off.
    private void OnSettingsChanged(CoveEvent coveEvent)
    {
        if (coveEvent.GetData<string>() != ModuleSwitches.SectionId)
        {
            return;
        }

        // Not awaited, so the bus isn't held up. ApplySwitchesAsync never throws.
        _ = ApplySwitchesAsync();
    }

    // Turns modules on or off to match the saved switches.
    private async Task ApplySwitchesAsync()
    {
        try
        {
            // Throws OperationCanceledException if Cove has started quitting.
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                var switches = _settings.Get<ModuleSwitches>(ModuleSwitches.SectionId);
                await Task.WhenAll(_modules.Select(module => ApplySwitchAsync(module, IsSwitchedOn(module, switches))));
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Cove is quitting, and StopAsync turns everything off anyway.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error while applying module switches");
        }
    }

    private Task ApplySwitchAsync(IModule module, bool switchedOn)
    {
        switch (GetState(module.Id), switchedOn)
        {
            case (ModuleState.Off, true):
                return TurnOnAsync(module);

            case (ModuleState.On, false):
                return TurnOffAsync(module);

            case (ModuleState.Failed, false):
                // It was already cleaned up when it failed. Marking it Off means switching it
                // back on is a fresh try.
                SetState(module, ModuleState.Off);
                Log.Information("Module {ModuleId} is off", module.Id);
                return Task.CompletedTask;

            default:
                // Already matches. A Failed module that's still switched on is left alone, so
                // saving other switches doesn't retry it over and over.
                return Task.CompletedTask;
        }
    }

    // Never throws: a module that fails is cleaned up, marked Failed and reported, and the rest carry on.
    private async Task TurnOnAsync(IModule module)
    {
        try
        {
            await module.EnableAsync(_lifetime.Token);
            SetState(module, ModuleState.On);
            Log.Information("Module {ModuleId} is on", module.Id);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Cove is quitting while this module was still starting. That's not a failure.
            await StopModuleAsync(module);
            SetState(module, ModuleState.Off);
        }
        catch (Exception ex)
        {
            // Exception messages can contain file paths, which only go in Debug logs (CLAUDE.md 10.3).
            Log.Error("Module {ModuleId} failed to start ({Error}) and was switched off", module.Id, ex.GetType().Name);
            Log.Debug(ex, "Module {ModuleId} failed to start", module.Id);

            // Give the module a chance to release anything it started before it threw.
            await StopModuleAsync(module);
            SetState(module, ModuleState.Failed);
            PublishFailed(module);
        }
    }

    private async Task TurnOffAsync(IModule module)
    {
        await StopModuleAsync(module);
        SetState(module, ModuleState.Off);
        Log.Information("Module {ModuleId} is off", module.Id);
    }

    // Never throws, and never waits longer than the stop time limit.
    private async Task StopModuleAsync(IModule module)
    {
        try
        {
            await module.DisableAsync().WaitAsync(_stopTimeLimit);
        }
        catch (TimeoutException)
        {
            Log.Warning("Module {ModuleId} didn't stop within {Seconds} s; moving on", module.Id, _stopTimeLimit.TotalSeconds);
        }
        catch (Exception ex)
        {
            Log.Error("Module {ModuleId} threw while stopping ({Error})", module.Id, ex.GetType().Name);
            Log.Debug(ex, "Module {ModuleId} threw while stopping", module.Id);
        }
    }

    private void SetState(IModule module, ModuleState state)
    {
        lock (_stateLock)
        {
            _states[module.Id] = state;
        }
    }

    private void PublishFailed(IModule module) =>
        _eventBus.Publish(new CoveEvent(
            CoveEventTypes.ModuleFailed,
            EventSource,
            // Failures show even during quiet time. JobFailed is the closest priority until the
            // contract has one for modules. TODO(K-08): switch to AlertPriority.ModuleFailed.
            AlertPriority.JobFailed,
            Message: $"{module.DisplayName} stopped working and was switched off for now.",
            Data: module.Id));
}
