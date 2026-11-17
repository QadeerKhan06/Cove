using System.IO;
using System.Windows;
using H.NotifyIcon;
using Serilog;

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

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");

        // false = leave Windows Efficiency Mode off. H.NotifyIcon turns it on by default, which drops
        // the whole process (and child processes such as ffmpeg) to idle priority.
        _trayIcon.ForceCreate(false);

        // The guard raises this on a thread-pool thread, so hop to the UI thread before doing anything.
        _singleInstance.ShowMenuRequested += (_, _) => Dispatcher.InvokeAsync(ShowMenu);
        _singleInstance.StartListening();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // TODO(C-03): disable every module here, before the tray icon goes away.

        // Stop listening first, so a Menu request can't arrive halfway through shutting down.
        _singleInstance?.Dispose();

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
