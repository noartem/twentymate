using System;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using TwentyMate.Core;

namespace TwentyMate.Views;

public partial class SettingsWindow : Window
{
    /// <summary>Monday-first display order, holding <see cref="DayOfWeek"/> indices (0 = Sunday).</summary>
    private static readonly int[] DayOrder = [1, 2, 3, 4, 5, 6, 0];

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
        UpdateFooterVersion();

        _controller.Scheduler.Ticked += OnTick;
        ThemeManager.Changed += OnThemeChanged;
        LocalizationManager.Changed += OnLanguageChanged;

        WindowEffects.Apply(this, BackdropType.Mica, ThemeManager.IsDark);
        UpdateStatus();
    }

    // ═══════════════ Loading and saving ═══════════════

    private void BuildDayChips()
    {
        foreach (var index in DayOrder)
        {
            var chip = new ToggleButton { Tag = index };
            chip.Classes.Add("day-chip");

            chip.Click += OnSettingChanged;
            _dayChips[index] = chip;
            DaysPanel.Children.Add(chip);
        }

        UpdateDayChipLabels();
    }

    /// <summary>Relabels the existing chip instances instead of rebuilding them, so click handlers and checked state survive a language switch.</summary>
    private void UpdateDayChipLabels()
    {
        var dayNames = LocalizationManager.Culture.DateTimeFormat.AbbreviatedDayNames;
        foreach (var index in DayOrder) _dayChips[index].Content = dayNames[index];
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
        LanguageCombo.SelectedIndex = (int)_settings.Language;

        _loading = false;
        UpdateWorkHoursAvailability();
        UpdateAutoPauseAvailability();
        UpdateUnitLabels();
        UpdateStats();
    }

    private void OnSettingChanged(object? sender, RoutedEventArgs e) => SaveToSettings();

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e) => SaveToSettings();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => SaveToSettings();

    private void OnTimeChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        // Silently roll back invalid input to the saved value.
        WorkStartBox.Text = NormalizeTime(WorkStartBox.Text ?? "", _settings.WorkStart);
        WorkEndBox.Text = NormalizeTime(WorkEndBox.Text ?? "", _settings.WorkEnd);
        SaveToSettings();
    }

    private static string NormalizeTime(string input, string fallback) =>
        TimeSpan.TryParse(input.Trim().Replace('.', ':'), CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1)
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
        _settings.WorkStart = NormalizeTime(WorkStartBox.Text ?? "", _settings.WorkStart);
        _settings.WorkEnd = NormalizeTime(WorkEndBox.Text ?? "", _settings.WorkEnd);
        for (var i = 0; i < 7; i++) _settings.WorkDays[i] = _dayChips[i].IsChecked is true;

        _settings.AutoPauseOnIdle = AutoPauseToggle.IsChecked is true;
        _settings.IdleThresholdMinutes = (int)IdleThresholdSlider.Value;

        _settings.LaunchAtLogin = AutostartToggle.IsChecked is true;
        _settings.Theme = (AppTheme)Math.Max(0, ThemeCombo.SelectedIndex);
        _settings.Language = (AppLanguage)Math.Max(0, LanguageCombo.SelectedIndex);

        _settings.Normalize();
        _controller.ApplySettings();

        UpdateWorkHoursAvailability();
        UpdateAutoPauseAvailability();
        UpdateUnitLabels();
        UpdateStatus();
    }

    /// <summary>Refreshes the 4 slider value labels — a DynamicResource can't be plugged into a string format, so these are set from code.</summary>
    private void UpdateUnitLabels()
    {
        IntervalValueText.Text = LocalizationManager.T("Unit_Minutes_ValueFormat", (int)IntervalSlider.Value);
        BreakValueText.Text = LocalizationManager.T("Unit_Seconds_ValueFormat", (int)BreakSlider.Value);
        PostponeValueText.Text = LocalizationManager.T("Unit_Minutes_ValueFormat", (int)PostponeSlider.Value);
        IdleThresholdValueText.Text = LocalizationManager.T("Unit_Minutes_ValueFormat", (int)IdleThresholdSlider.Value);
    }

    private void UpdateFooterVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
        VersionText.Text = LocalizationManager.T("Settings_Footer_Version", versionText);
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
                StatusTitle.Text = LocalizationManager.T("Settings_Status_BreakRemaining", remaining);
                StatusSubtitle.Text = LocalizationManager.T("Settings_Status_BreakSubtitle");
                StatusRing.Value = 1 - scheduler.Progress;
                break;

            case SchedulerState.Paused:
                StatusTitle.Text = scheduler.IsPausedByIdle
                    ? LocalizationManager.T("Status_PausedByIdle")
                    : scheduler.PausedUntil is { } until
                        ? LocalizationManager.T("Status_PausedUntil", until.ToString("HH:mm", CultureInfo.InvariantCulture))
                        : LocalizationManager.T("Settings_Status_PausedIndefinite");
                StatusSubtitle.Text = LocalizationManager.T("Settings_Status_PausedSubtitle");
                StatusRing.Value = 0;
                break;

            case SchedulerState.OffHours:
                StatusTitle.Text = LocalizationManager.T("Status_OffHours");
                StatusSubtitle.Text = LocalizationManager.T("Settings_Status_OffHoursSubtitle", _settings.WorkStart, _settings.WorkEnd);
                StatusRing.Value = 0;
                break;

            default:
                StatusTitle.Text = LocalizationManager.T("Settings_Status_NextBreak", remaining);
                StatusSubtitle.Text = LocalizationManager.T("Settings_Status_NextBreakSubtitle");
                StatusRing.Value = scheduler.Progress;
                break;
        }

        PauseButton.Content = scheduler.State is SchedulerState.Paused
            ? LocalizationManager.T("Action_Resume")
            : LocalizationManager.T("Action_Pause");
    }

    private void UpdateStats()
    {
        var stats = _controller.Stats;
        var today = stats.LastBreakDay == DateTime.Today.ToString("yyyy-MM-dd")
            ? stats.BreaksToday
            : 0;

        StatsText.Text = LocalizationManager.T("Settings_Stats_Format", today, stats.BreaksTotal);
    }

    // ═══════════════ Buttons ═══════════════

    private void OnBreakNow(object? sender, RoutedEventArgs e) => _controller.Scheduler.StartBreakNow();

    private void OnTogglePause(object? sender, RoutedEventArgs e)
    {
        _controller.Scheduler.TogglePause();
        UpdateStatus();
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        var dialog = new FAContentDialog
        {
            Title = "TwentyMate",
            Content = LocalizationManager.T("Settings_Reset_ConfirmBody"),
            PrimaryButtonText = LocalizationManager.T("Settings_Reset_Button"),
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };

        if (await dialog.ShowAsync(this) is not FAContentDialogResult.Primary) return;

        // Usage history (AppStats) lives outside AppSettings entirely now, so resetting
        // settings to defaults no longer needs to carry it forward by hand.
        var defaults = new AppSettings();

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
        target.Language = source.Language;
    }

    // ═══════════════ Window styling ═══════════════

    /// <summary>The custom title bar replaces the system-drawn one, so it has to opt back into drag-to-move itself.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero) WindowEffects.SetDarkMode(handle, ThemeManager.IsDark);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateDayChipLabels();
        UpdateUnitLabels();
        UpdateFooterVersion();
        UpdateStatus();
        UpdateStats();
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.Scheduler.Ticked -= OnTick;
        ThemeManager.Changed -= OnThemeChanged;
        LocalizationManager.Changed -= OnLanguageChanged;
        base.OnClosed(e);
    }
}
