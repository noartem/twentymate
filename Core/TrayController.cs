using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TwentyMate.Views;
using TrayIcon = TwentyMate.Platform.TrayIcon;

namespace TwentyMate.Core;

/// <summary>
/// Wires up the scheduler, the tray icon, and the app's windows.
/// This is the single place that decides what to show the user.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly AppStats _stats;
    private readonly BreakScheduler _scheduler;
    private readonly TrayIconFactory _iconFactory = new();
    private readonly TrayIcon _trayIcon = new();
    private readonly List<BreakWindow> _breakWindows = [];

    // Screens.All needs a realized native window to ask, but there otherwise isn't one until
    // an overlay is shown — this window is never shown, it exists purely to answer that query.
    private readonly Window _screenProbe = new() { ShowInTaskbar = false, WindowDecorations = WindowDecorations.None };

    private SettingsWindow? _settingsWindow;
    private TrayMenuWindow? _menuWindow;
    private DateTime _menuClosedAt = DateTime.MinValue;

    public TrayController(AppSettings settings)
    {
        _settings = settings;
        _stats = StatsStore.Load();
        _scheduler = new BreakScheduler(settings);

        _trayIcon.Clicked += ToggleMenu;
        _trayIcon.BalloonClicked += () => _scheduler.StartBreakNow();

        _scheduler.Ticked += (_, _) => RefreshIcon();
        _scheduler.StateChanged += (_, _) => RefreshIcon();
        _scheduler.BreakStarted += (_, _) => OnBreakStarted();
        _scheduler.BreakFinished += (_, completed) => OnBreakFinished(completed);

        ThemeManager.Changed += (_, _) => RefreshIcon();
        LocalizationManager.Changed += (_, _) => RefreshIcon();
    }

    public BreakScheduler Scheduler => _scheduler;
    public AppStats Stats => _stats;

    public void Start()
    {
        // The installer enables autostart via the Run key before the first launch, when the
        // settings file doesn't exist yet. Treat the key as the source of truth, otherwise
        // we'd wipe it out right here.
        if (!_settings.LaunchAtLogin && StartupManager.IsEnabled)
            _settings.LaunchAtLogin = true;

        StartupManager.TrySet(_settings.LaunchAtLogin);
        _scheduler.Start();
        RefreshIcon();
    }

    /// <summary>Called by the settings window after parameters change.</summary>
    public void ApplySettings()
    {
        _scheduler.ApplySettings(_settings);
        StartupManager.TrySet(_settings.LaunchAtLogin);
        ThemeManager.Apply(_settings.Theme);
        LocalizationManager.Apply(_settings.Language);
        SettingsStore.Save(_settings);
        RefreshIcon();
    }

    // ═══════════════ Icon and tooltip ═══════════════

    private void RefreshIcon()
    {
        _trayIcon.SetIcon(_iconFactory.Get(_scheduler.State, _scheduler.Progress));
        _trayIcon.SetTooltip(BuildTooltip());
    }

    private string BuildTooltip()
    {
        var text = _scheduler.State switch
        {
            SchedulerState.Break => LocalizationManager.T("Tray_Tooltip_Break", FormatClock(_scheduler.Remaining)),
            SchedulerState.Paused when _scheduler.IsPausedByIdle => LocalizationManager.T("Status_PausedByIdle"),
            SchedulerState.Paused when _scheduler.PausedUntil is { } until =>
                LocalizationManager.T("Status_PausedUntil", until.ToString("HH:mm", CultureInfo.InvariantCulture)),
            SchedulerState.Paused => LocalizationManager.T("Status_Paused"),
            SchedulerState.OffHours => LocalizationManager.T("Status_OffHours"),
            _ => LocalizationManager.T("Tray_Tooltip_NextBreak", FormatClock(_scheduler.Remaining)),
        };

        // Windows truncates the tray icon tooltip at 127 characters.
        return $"TwentyMate\n{text}";
    }

    public static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    // ═══════════════ Break ═══════════════

    private void OnBreakStarted()
    {
        if (_settings.SoundOnBreakStart) SoundService.PlayBreakStart();

        switch (_settings.Style)
        {
            case NotificationStyle.SystemToast:
                _trayIcon.ShowBalloon(
                    LocalizationManager.T("Tray_Balloon_Title"),
                    LocalizationManager.T("Tray_Balloon_Body", _settings.BreakSeconds));
                break;

            case NotificationStyle.Overlay:
                ShowOverlay();
                break;

            case NotificationStyle.TrayIconOnly:
                // The changed icon is enough on its own.
                break;
        }
    }

    private void OnBreakFinished(bool completed)
    {
        CloseOverlay();

        if (!completed) return;

        if (_settings.SoundOnBreakEnd) SoundService.PlayBreakEnd();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_stats.LastBreakDay != today)
        {
            _stats.LastBreakDay = today;
            _stats.BreaksToday = 0;
        }

        _stats.BreaksToday++;
        _stats.BreaksTotal++;
        StatsStore.Save(_stats);
    }

    private void ShowOverlay()
    {
        CloseOverlay();

        var allScreens = _screenProbe.Screens?.All ?? [];
        var primary = allScreens.FirstOrDefault(s => s.IsPrimary) ?? allScreens.FirstOrDefault();
        var screens = _settings.DimAllScreens || primary is null
            ? allScreens
            : new[] { primary };

        foreach (var screen in screens)
        {
            var isPrimary = screen.IsPrimary || screens.Count == 1;
            var window = new BreakWindow(_settings, _scheduler, showControls: isPrimary);
            _breakWindows.Add(window);
            window.ShowOn(screen);
        }
    }

    private void CloseOverlay()
    {
        foreach (var window in _breakWindows.ToArray()) window.CloseOverlay();
        _breakWindows.Clear();
    }

    // ═══════════════ Menu and windows ═══════════════

    private void ToggleMenu()
    {
        // A click on the icon first removes focus from the open menu, which closes it on its
        // own. We treat the short window right after that as a "second click" and don't
        // reopen the menu.
        if (_menuWindow is not null || DateTime.Now - _menuClosedAt < TimeSpan.FromMilliseconds(300))
            return;

        _menuWindow = new TrayMenuWindow(_settings, _scheduler, this);
        _menuWindow.Closed += (_, _) =>
        {
            _menuWindow = null;
            _menuClosedAt = DateTime.Now;
        };
        _menuWindow.ShowNearTray();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState is WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void Quit()
    {
        SettingsStore.Save(_settings);
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose()
    {
        _scheduler.Stop();
        CloseOverlay();
        _trayIcon.Dispose();
        _iconFactory.Dispose();
        _screenProbe.Close();
    }
}
