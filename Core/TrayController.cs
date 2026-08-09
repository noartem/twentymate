using System;
using System.Collections.Generic;
using System.Windows;
using TwentyMate.Views;
using Forms = System.Windows.Forms;

namespace TwentyMate.Core;

/// <summary>
/// Связывает планировщик, значок в трее и окна приложения.
/// Это единственное место, которое решает, что показать пользователю.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly BreakScheduler _scheduler;
    private readonly TrayIconFactory _iconFactory = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly List<BreakWindow> _breakWindows = [];

    private SettingsWindow? _settingsWindow;
    private TrayMenuWindow? _menuWindow;
    private DateTime _menuClosedAt = DateTime.MinValue;

    public TrayController(AppSettings settings)
    {
        _settings = settings;
        _scheduler = new BreakScheduler(settings);

        _notifyIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "TwentyMate",
        };

        _notifyIcon.MouseUp += OnTrayMouseUp;
        _notifyIcon.BalloonTipClicked += (_, _) => _scheduler.StartBreakNow();

        _scheduler.Ticked += (_, _) => RefreshIcon();
        _scheduler.StateChanged += (_, _) => RefreshIcon();
        _scheduler.BreakStarted += (_, _) => OnBreakStarted();
        _scheduler.BreakFinished += (_, completed) => OnBreakFinished(completed);

        ThemeManager.Changed += (_, _) => RefreshIcon();
    }

    public BreakScheduler Scheduler => _scheduler;

    public void Start()
    {
        // Установщик включает автозапуск ключом Run ещё до первого запуска, когда файла
        // настроек нет. Принимаем ключ за источник истины, иначе стёрли бы его здесь же.
        if (!_settings.LaunchAtLogin && StartupManager.IsEnabled)
            _settings.LaunchAtLogin = true;

        StartupManager.TrySet(_settings.LaunchAtLogin);
        _scheduler.Start();
        RefreshIcon();
    }

    /// <summary>Вызывается окном настроек после изменения параметров.</summary>
    public void ApplySettings()
    {
        _scheduler.ApplySettings(_settings);
        StartupManager.TrySet(_settings.LaunchAtLogin);
        ThemeManager.Apply(_settings.Theme);
        SettingsStore.Save(_settings);
        RefreshIcon();
    }

    // ═══════════════ Значок и подсказка ═══════════════

    private void RefreshIcon()
    {
        _notifyIcon.Icon = _iconFactory.Get(_scheduler.State, _scheduler.Progress);
        _notifyIcon.Text = BuildTooltip();
    }

    private string BuildTooltip()
    {
        var text = _scheduler.State switch
        {
            SchedulerState.Break => $"Перерыв — {FormatClock(_scheduler.Remaining)}",
            SchedulerState.Paused when _scheduler.IsPausedByIdle => "Пауза — нет активности",
            SchedulerState.Paused when _scheduler.PausedUntil is { } until =>
                $"Пауза до {until:HH:mm}",
            SchedulerState.Paused => "Пауза",
            SchedulerState.OffHours => "Вне рабочих часов",
            _ => $"Следующий перерыв через {FormatClock(_scheduler.Remaining)}",
        };

        // Windows обрезает подсказку значка на 127 символах.
        return $"TwentyMate\n{text}";
    }

    public static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    // ═══════════════ Перерыв ═══════════════

    private void OnBreakStarted()
    {
        if (_settings.SoundOnBreakStart) SoundService.PlayBreakStart();

        switch (_settings.Style)
        {
            case NotificationStyle.SystemToast:
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "Время для глаз",
                    $"Посмотрите вдаль {_settings.BreakSeconds} секунд — на объект метрах в шести.",
                    Forms.ToolTipIcon.None);
                break;

            case NotificationStyle.Overlay:
                ShowOverlay();
                break;

            case NotificationStyle.TrayIconOnly:
                // Достаточно изменившегося значка.
                break;
        }
    }

    private void OnBreakFinished(bool completed)
    {
        CloseOverlay();

        if (!completed) return;

        if (_settings.SoundOnBreakEnd) SoundService.PlayBreakEnd();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.LastBreakDay != today)
        {
            _settings.LastBreakDay = today;
            _settings.BreaksToday = 0;
        }

        _settings.BreaksToday++;
        _settings.BreaksTotal++;
        SettingsStore.Save(_settings);
    }

    private void ShowOverlay()
    {
        CloseOverlay();

        var screens = _settings.DimAllScreens
            ? Forms.Screen.AllScreens
            : [Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]];

        foreach (var screen in screens)
        {
            var isPrimary = screen.Primary || screens.Length == 1;
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

    // ═══════════════ Меню и окна ═══════════════

    private void OnTrayMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right)
            ToggleMenu();
    }

    private void ToggleMenu()
    {
        // Клик по значку сначала снимает фокус с открытого меню, и оно закрывается само.
        // Короткое окно после этого считаем «вторым щелчком» и меню не открываем заново.
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
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _scheduler.Stop();
        CloseOverlay();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconFactory.Dispose();
    }
}
