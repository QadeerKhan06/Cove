using System.IO;
using System.Windows;
using Cove.Core.Contracts;
using Cove.UI.Pill;
using H.NotifyIcon;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Appearance;
#if DEBUG
using System.Windows.Input;
using Cove.UI.DevTools;
#endif

namespace Cove.App;

/// <summary>
/// Cove's entry point. Starts as a tray icon only: no main window and no taskbar button.
/// </summary>
public partial class App : Application
{
    // Shared by every copy of Cove. The GUID keeps it from clashing with another app called "Cove".
    private const string AppId = "Cove-99b2b746-87d8-442a-a7d4-cdd7a6077ca3";

    // One log file per day; a week is enough for a bug report without filling the disk.
    private const int LogFilesToKeep = 7;

    // Fields, not locals: both must live as long as the app. If the guard were garbage-collected,
    // its mutex would close and the next launch would wrongly think it was the first copy.
    private SingleInstanceGuard? _singleInstance;
    private TaskbarIcon? _trayIcon;
    private PillWindow? _pill;
    private PillController? _pillController;
#if DEBUG
    private DebugHotKey? _testPanelShortcut;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard(AppId);
        if (!_singleInstance.IsFirstInstance)
        {
            // Cove is already running: ask that copy to open the Menu, then quit without showing anything.
            _singleInstance.SignalFirstInstance();
            Shutdown();
            return;
        }

        StartLogging();
        Log.Information("Cove started");

        // Match Windows' light or dark mode and accent colour before any window is created, and keep
        // matching it when the user switches.
        ApplicationThemeManager.ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");

        // false = leave Windows Efficiency Mode off. H.NotifyIcon turns it on by default, which drops
        // the whole process (and child processes such as ffmpeg) to idle priority.
        _trayIcon.ForceCreate(false);

        // The pill starts hidden: nothing is on screen until it's called or an event needs the user.
        _pill = new PillWindow();

        // WPF makes the first window an app creates its MainWindow, and WPF-UI restyles MainWindow's
        // background on every theme change, which paints the pill's see-through corners. The pill is
        // never a main window; the next real window (such as Settings) becomes MainWindow instead.
        MainWindow = null;
        _pillController = new PillController(_pill, CreatePanels());
#if DEBUG
        AddTestAlertMenuItem();
        _testPanelShortcut = new DebugHotKey(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.P, ToggleTestPanel);
#endif

        // The guard raises this on a thread-pool thread, so hop to the UI thread before doing anything.
        _singleInstance.ShowMenuRequested += (_, _) => Dispatcher.InvokeAsync(ShowMenu);
        _singleInstance.StartListening();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // TODO(C-03): disable every module here, before the tray icon goes away.

        // Stop listening first, so a Menu request can't arrive halfway through shutting down.
        _singleInstance?.Dispose();

        // A static event: unsubscribing lets the app be cleaned up properly.
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

#if DEBUG
        _testPanelShortcut?.Dispose();
#endif

        // Close an open panel first, so its module hears OnClosed.
        _pillController?.ClosePanel();
        _pill?.Close();

        // Removes the icon now; otherwise a "ghost" icon stays in the tray until the mouse passes over it.
        _trayIcon?.Dispose();

        Log.Information("Cove stopped");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void ShowMenu()
    {
        // TODO(C-12): open the Cove key Menu. Until then, the log line shows the request arrived.
        Log.Information("Menu requested by a second launch");
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Log.Information("Quit chosen from the tray menu");
        Shutdown();
    }

    // Switching Windows between light and dark arrives as a "General" preference change.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        // SystemEvents may raise this off the UI thread; the theme has to change on it.
        Dispatcher.InvokeAsync(() => ApplicationThemeManager.ApplySystemTheme());
    }

    // TODO(C-03): add the panels of every switched-on module once the module loader is wired in.
    private IPanel[] CreatePanels()
    {
#if DEBUG
        return [new TestPanel(close: () => _pillController?.ClosePanel())];
#else
        return [];
#endif
    }

#if DEBUG
    // TODO(C-12): remove the test alert and the test panel once real alerts and the Menu show the
    // pill. The delay leaves time to click into another app first, to check that the alert doesn't
    // take focus when it appears (clicking the tray menu itself always does).
    private static readonly TimeSpan TestAlertDelay = TimeSpan.FromSeconds(3);

    // The design's alert time (SPEC.md §5); C-07 brings the real countdown.
    private static readonly TimeSpan TestAlertDuration = TimeSpan.FromSeconds(4);

    private void AddTestAlertMenuItem()
    {
        var item = new System.Windows.Controls.MenuItem { Header = "Show test alert in 3 s" };
        item.Click += (_, _) => RunAfter(TestAlertDelay, ShowTestAlert);
        _trayIcon?.ContextMenu?.Items.Insert(0, item);
        _trayIcon?.ContextMenu?.Items.Insert(1, new System.Windows.Controls.Separator());
    }

    private void ShowTestAlert()
    {
        if (_pillController?.ShowAlert("IMG_2041.heic downloaded") == true)
        {
            RunAfter(TestAlertDuration, () => _pillController.HideAlert());
        }
    }

    // Ctrl+Alt+Shift+P: opens the test panel, or closes it if it's open.
    private void ToggleTestPanel()
    {
        if (_pillController is null)
        {
            return;
        }

        if (_pillController.State == PillState.Panel)
        {
            _pillController.ClosePanel();
        }
        else
        {
            _pillController.OpenPanel(TestPanel.PanelId);
        }
    }

    private static void RunAfter(TimeSpan delay, Action action)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
#endif

    private static void StartLogging()
    {
        string logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cove", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logFolder, "cove-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: LogFilesToKeep)
            .CreateLogger();
    }
}
