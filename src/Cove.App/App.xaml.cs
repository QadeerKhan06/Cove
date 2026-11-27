using System.IO;
using System.Windows;
using Cove.UI.Pill;
using H.NotifyIcon;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Appearance;

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
#if DEBUG
        AddTestPillMenuItem();
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

#if DEBUG
    // TODO(C-12): remove once Peek, Alerts and the Menu show the pill for real; until then this is
    // the only way to see it. The delay leaves time to click into another app first, to check that
    // the pill doesn't take focus when it appears (clicking the tray menu itself always does).
    private static readonly TimeSpan TestPillDelay = TimeSpan.FromSeconds(3);

    private void AddTestPillMenuItem()
    {
        var item = new System.Windows.Controls.MenuItem { Header = "Show test pill in 3 s" };
        item.Click += (_, _) => ToggleTestPill(item);
        _trayIcon?.ContextMenu?.Items.Insert(0, item);
        _trayIcon?.ContextMenu?.Items.Insert(1, new System.Windows.Controls.Separator());
    }

    private void ToggleTestPill(System.Windows.Controls.MenuItem item)
    {
        var pill = _pill;
        if (pill is null)
        {
            return;
        }

        if (pill.IsVisible)
        {
            pill.Hide();
            item.Header = "Show test pill in 3 s";
            return;
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TestPillDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            pill.Show();
        };
        timer.Start();
        item.Header = "Hide test pill";
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
