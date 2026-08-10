using System;
using System.Linq;
using System.Threading;
using System.Windows;
using TwentyMate.Core;
using TwentyMate.Views;

namespace TwentyMate;

public partial class App : Application
{
    private const string MutexName = "TwentyMate.SingleInstance.v1";

    private Mutex? _instanceMutex;
    private TrayController? _tray;

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // A second instance isn't needed: the app lives in the tray.
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // A background utility shouldn't die from a failure in one window: log the
        // cause and keep tracking time.
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        Settings = SettingsStore.Load();
        ThemeManager.Initialize(Settings.Theme);

        _tray = new TrayController(Settings);
        _tray.Start();

        // On first launch, show settings right away so the app doesn't
        // look like "nothing happened".
        var startedByAutostart = e.Args.Contains("--minimized");
        if (!Settings.FirstRunDone && !startedByAutostart)
        {
            Settings.FirstRunDone = true;
            SettingsStore.Save(Settings);
            _tray.ShowSettings();
        }
    }

    private static void LogError(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Directory);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(SettingsStore.Directory, "error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging failing isn't a reason for a second crash.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Save(Settings);
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
