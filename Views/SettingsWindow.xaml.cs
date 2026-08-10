using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using TwentyMate.Core;

namespace TwentyMate.Views;

public partial class SettingsWindow : Window
{
    /// <summary>Day labels in the familiar Monday-first order, with their <see cref="DayOfWeek"/> indices.</summary>
    private static readonly (string Label, int Index)[] Days =
    [
        ("Пн", 1), ("Вт", 2), ("Ср", 3), ("Чт", 4), ("Пт", 5), ("Сб", 6), ("Вс", 0),
    ];

    private readonly AppSettings _settings;
    private readonly TrayController _controller;
    private readonly ToggleButton[] _dayChips = new ToggleButton[7];

    /// <summary>
    /// While XAML parsing and control population are in progress, change handlers must not
    /// fire: otherwise the templates' default values would overwrite the user's settings.
    /// </summary>
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, TrayController controller)
    {
        _settings = settings;
        _controller = controller;

        InitializeComponent();

        BuildDayChips();
        LoadFromSettings();

        _controller.Scheduler.Ticked += OnTick;
        ThemeManager.Changed += OnThemeChanged;

        WindowEffects.Apply(this, BackdropType.Mica, ThemeManager.IsDark);
        UpdateStatus();
    }

    // ═══════════════ Loading and saving ═══════════════

    private void BuildDayChips()
    {
        foreach (var (label, index) in Days)
        {
            var chip = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("DayChip"),
                Tag = index,
            };

            chip.Click += OnSettingChanged;
            _dayChips[index] = chip;
            DaysPanel.Children.Add(chip);
        }
    }

    private void LoadFromSettings()
    {
        _loading = true;

        IntervalSlider.Value = _settings.IntervalMinutes;
        BreakSlider.Value = _settings.BreakSeconds;
        PostponeSlider.Value = _settings.PostponeMinutes;

        StyleCombo.SelectedIndex = (int)_settings.Style;
        SoundStartToggle.IsChecked = _settings.SoundOnBreakStart;
        SoundEndToggle.IsChecked = _settings.SoundOnBreakEnd;
        AllowSkipToggle.IsChecked = _settings.AllowSkip;
        DimAllToggle.IsChecked = _settings.DimAllScreens;

        WorkHoursToggle.IsChecked = _settings.WorkingHoursEnabled;
        WorkStartBox.Text = _settings.WorkStart;
        WorkEndBox.Text = _settings.WorkEnd;
        for (var i = 0; i < 7; i++) _dayChips[i].IsChecked = _settings.WorkDays[i];

        AutoPauseToggle.IsChecked = _settings.AutoPauseOnIdle;
        IdleThresholdSlider.Value = _settings.IdleThresholdMinutes;

        AutostartToggle.IsChecked = _settings.LaunchAtLogin || StartupManager.IsEnabled;
        ThemeCombo.SelectedIndex = (int)_settings.Theme;

        _loading = false;
        UpdateWorkHoursAvailability();
        UpdateAutoPauseAvailability();
        UpdateStats();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e) => SaveToSettings();

    private void OnSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveToSettings();

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e) => SaveToSettings();

    private void OnTimeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        // Silently roll back invalid input to the saved value.
        WorkStartBox.Text = NormalizeTime(WorkStartBox.Text, _settings.WorkStart);
        WorkEndBox.Text = NormalizeTime(WorkEndBox.Text, _settings.WorkEnd);
        SaveToSettings();
    }

    private static string NormalizeTime(string input, string fallback) =>
        TimeSpan.TryParse(input.Trim().Replace('.', ':'), out var time) && time < TimeSpan.FromDays(1)
            ? $"{time.Hours:00}:{time.Minutes:00}"
            : fallback;

    private void SaveToSettings()
    {
        if (_loading) return;

        _settings.IntervalMinutes = (int)IntervalSlider.Value;
        _settings.BreakSeconds = (int)BreakSlider.Value;
        _settings.PostponeMinutes = (int)PostponeSlider.Value;

        _settings.Style = (NotificationStyle)Math.Max(0, StyleCombo.SelectedIndex);
        _settings.SoundOnBreakStart = SoundStartToggle.IsChecked is true;
        _settings.SoundOnBreakEnd = SoundEndToggle.IsChecked is true;
        _settings.AllowSkip = AllowSkipToggle.IsChecked is true;
        _settings.DimAllScreens = DimAllToggle.IsChecked is true;

        _settings.WorkingHoursEnabled = WorkHoursToggle.IsChecked is true;
        _settings.WorkStart = NormalizeTime(WorkStartBox.Text, _settings.WorkStart);
        _settings.WorkEnd = NormalizeTime(WorkEndBox.Text, _settings.WorkEnd);
        for (var i = 0; i < 7; i++) _settings.WorkDays[i] = _dayChips[i].IsChecked is true;

        _settings.AutoPauseOnIdle = AutoPauseToggle.IsChecked is true;
        _settings.IdleThresholdMinutes = (int)IdleThresholdSlider.Value;

        _settings.LaunchAtLogin = AutostartToggle.IsChecked is true;
        _settings.Theme = (AppTheme)Math.Max(0, ThemeCombo.SelectedIndex);

        _settings.Normalize();
        _controller.ApplySettings();

        UpdateWorkHoursAvailability();
        UpdateAutoPauseAvailability();
        UpdateStatus();
    }

    private void UpdateWorkHoursAvailability()
    {
        var enabled = WorkHoursToggle.IsChecked is true;
        WorkHoursPanel.IsEnabled = enabled;
        WorkHoursPanel.Opacity = enabled ? 1 : 0.45;
    }

    private void UpdateAutoPauseAvailability()
    {
        var enabled = AutoPauseToggle.IsChecked is true;
        AutoPausePanel.IsEnabled = enabled;
        AutoPausePanel.Opacity = enabled ? 1 : 0.45;
    }

    // ═══════════════ Status ═══════════════

    private void OnTick(object? sender, EventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        var scheduler = _controller.Scheduler;
        var remaining = TrayController.FormatClock(scheduler.Remaining);

        switch (scheduler.State)
        {
            case SchedulerState.Break:
                StatusTitle.Text = $"Перерыв: {remaining}";
                StatusSubtitle.Text = "Смотрите вдаль и моргайте";
                StatusRing.Value = 1 - scheduler.Progress;
                break;

            case SchedulerState.Paused:
                StatusTitle.Text = scheduler.IsPausedByIdle
                    ? "Пауза — нет активности"
                    : scheduler.PausedUntil is { } until
                        ? $"Пауза до {until:HH:mm}"
                        : "Напоминания на паузе";
                StatusSubtitle.Text = "Нажмите «Продолжить», когда вернётесь";
                StatusRing.Value = 0;
                break;

            case SchedulerState.OffHours:
                StatusTitle.Text = "Вне рабочих часов";
                StatusSubtitle.Text = $"Расписание: {_settings.WorkStart}–{_settings.WorkEnd}";
                StatusRing.Value = 0;
                break;

            default:
                StatusTitle.Text = $"Перерыв через {remaining}";
                StatusSubtitle.Text = "Правило 20-20-20 бережёт глаза";
                StatusRing.Value = scheduler.Progress;
                break;
        }

        PauseButton.Content = scheduler.State is SchedulerState.Paused ? "Продолжить" : "Пауза";
    }

    private void UpdateStats()
    {
        var today = _settings.LastBreakDay == DateTime.Today.ToString("yyyy-MM-dd")
            ? _settings.BreaksToday
            : 0;

        StatsText.Text = $"Перерывов сегодня: {today} · всего: {_settings.BreaksTotal}";
    }

    // ═══════════════ Buttons ═══════════════

    private void OnBreakNow(object sender, RoutedEventArgs e) => _controller.Scheduler.StartBreakNow();

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _controller.Scheduler.TogglePause();
        UpdateStatus();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Вернуть все настройки к значениям по умолчанию?",
            "TwentyMate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer is not MessageBoxResult.Yes) return;

        var defaults = new AppSettings
        {
            // Keep the counters and the first-run flag — that's history, not a setting.
            FirstRunDone = true,
            BreaksToday = _settings.BreaksToday,
            BreaksTotal = _settings.BreaksTotal,
            LastBreakDay = _settings.LastBreakDay,
        };

        CopyInto(defaults, _settings);
        LoadFromSettings();
        _controller.ApplySettings();
        UpdateStatus();
    }

    private static void CopyInto(AppSettings source, AppSettings target)
    {
        target.IntervalMinutes = source.IntervalMinutes;
        target.BreakSeconds = source.BreakSeconds;
        target.PostponeMinutes = source.PostponeMinutes;
        target.Style = source.Style;
        target.SoundOnBreakStart = source.SoundOnBreakStart;
        target.SoundOnBreakEnd = source.SoundOnBreakEnd;
        target.AllowSkip = source.AllowSkip;
        target.DimAllScreens = source.DimAllScreens;
        target.WorkingHoursEnabled = source.WorkingHoursEnabled;
        target.WorkStart = source.WorkStart;
        target.WorkEnd = source.WorkEnd;
        target.WorkDays = (bool[])source.WorkDays.Clone();
        target.AutoPauseOnIdle = source.AutoPauseOnIdle;
        target.IdleThresholdMinutes = source.IdleThresholdMinutes;
        target.LaunchAtLogin = source.LaunchAtLogin;
        target.Theme = source.Theme;
    }

    // ═══════════════ Window styling ═══════════════

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) WindowEffects.SetDarkMode(handle, ThemeManager.IsDark);
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.Scheduler.Ticked -= OnTick;
        ThemeManager.Changed -= OnThemeChanged;
        base.OnClosed(e);
    }
}
