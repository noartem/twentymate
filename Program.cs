using System;
using System.Threading;
using Avalonia;

namespace TwentyMate;

internal static class Program
{
    private const string MutexName = "TwentyMate.SingleInstance.v1";

    [STAThread]
    public static void Main(string[] args)
    {
        // A second instance isn't needed: the app lives in the tray. The mutex is held for the
        // lifetime of Main, which doesn't return until StartWithClassicDesktopLifetime does.
        using var instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
