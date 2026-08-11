using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TwentyMate.Core;

namespace TwentyMate;

public partial class App : Application
{
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // A background utility shouldn't die from a failure in one window: log the
            // cause and keep tracking time.
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                LogError(args.Exception);
                args.Handled = true;
            };

            var settings = SettingsStore.Load();
            ThemeManager.Initialize(settings.Theme);
            LocalizationManager.Initialize(settings.Language);

            _tray = new TrayController(settings);
            _tray.Start();

            // On first launch, show settings right away so the app doesn't
            // look like "nothing happened".
            var startedByAutostart = desktop.Args?.Contains("--minimized") ?? false;
            if (!_tray.Stats.FirstRunDone && !startedByAutostart)
            {
                _tray.Stats.FirstRunDone = true;
                StatsStore.Save(_tray.Stats);
                _tray.ShowSettings();
            }

            desktop.ShutdownRequested += (_, _) =>
            {
                SettingsStore.Save(settings);
                StatsStore.Save(_tray.Stats);
                _tray.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
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
}
