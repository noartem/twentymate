using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using TwentyMate.Core;

namespace TwentyMate.Views;

/// <summary>
/// The tray icon's popup menu — a custom take instead of the system
/// context menu, to fit in with Windows 11 styling.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInt32 point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInt32
    {
        public int X;
        public int Y;
    }

    private readonly AppSettings _settings;
    private readonly BreakScheduler _scheduler;
    private readonly TrayController _controller;

    private bool _closing;

    public TrayMenuWindow(AppSettings settings, BreakScheduler scheduler, TrayController controller)
    {
        _settings = settings;
        _scheduler = scheduler;
        _controller = controller;

        InitializeComponent();

        WindowEffects.ApplyTextRendering(this);

        _scheduler.Ticked += OnTick;
        Deactivated += (_, _) => CloseMenu();

        UpdateState();
    }

    public void ShowNearTray()
    {
        Show();

        // Dimensions are only known after Show with SizeToContent.
        Dispatcher.UIThread.Post(PositionNearCursor, DispatcherPriority.Loaded);

        Activate();

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(140),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        _ = fade.RunAsync(this);
    }

    private void PositionNearCursor()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !GetCursorPos(out var cursor)) return;

        var screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen is null) return;

        var work = screen.WorkingArea;

        // Bounds is laid out (post SizeToContent) but in DIPs — convert to this window's
        // physical pixels before working in screen coordinates.
        var width = (int)Math.Ceiling(Bounds.Width * DesktopScaling);
        var height = (int)Math.Ceiling(Bounds.Height * DesktopScaling);

        // Snap to whichever corner of the working area the taskbar is on.
        var x = Math.Clamp(cursor.X - width / 2, work.X, work.Right - width);
        var y = cursor.Y > work.Y + work.Height / 2
            ? work.Bottom - height
            : work.Y;

        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpShowWindow);
    }

    private void OnTick(object? sender, EventArgs e) => UpdateState();

    private void UpdateState()
    {
        var remaining = TrayController.FormatClock(_scheduler.Remaining);

        switch (_scheduler.State)
        {
            case SchedulerState.Break:
                TimeText.Text = remaining;
                StatusText.Text = LocalizationManager.T("Tray_Status_BreakOngoing");
                RingGlyph.Text = "";
                Ring.Value = 1 - _scheduler.Progress;
                BreakNowText.Text = LocalizationManager.T("Tray_BreakNow_Skip");
                break;

            case SchedulerState.Paused:
                TimeText.Text = _scheduler.PausedUntil is { } until
                    ? LocalizationManager.T("Tray_Status_PausedUntilTime", until.ToString("HH:mm", CultureInfo.InvariantCulture))
                    : LocalizationManager.T("Tray_Status_PausedIndefiniteTime");
                StatusText.Text = LocalizationManager.T("Tray_Status_PausedSubtitle");
                RingGlyph.Text = "";
                Ring.Value = 0;
                BreakNowText.Text = LocalizationManager.T("Tray_BreakNow_Default");
                break;

            case SchedulerState.OffHours:
                TimeText.Text = LocalizationManager.T("Tray_Status_OffHoursTime");
                StatusText.Text = LocalizationManager.T("Tray_Status_OffHoursSubtitle", _settings.WorkStart, _settings.WorkEnd);
                RingGlyph.Text = "";
                Ring.Value = 0;
                BreakNowText.Text = LocalizationManager.T("Tray_BreakNow_Default");
                break;

            default:
                TimeText.Text = remaining;
                StatusText.Text = LocalizationManager.T("Tray_Status_NextBreak");
                RingGlyph.Text = "";
                Ring.Value = _scheduler.Progress;
                BreakNowText.Text = LocalizationManager.T("Tray_BreakNow_Default");
                break;
        }

        var paused = _scheduler.State is SchedulerState.Paused;
        PauseText.Text = paused ? LocalizationManager.T("Action_Resume") : LocalizationManager.T("Action_Pause");
        PauseGlyph.Text = paused ? "" : "";
        PausePresets.IsVisible = !paused;

        var stats = _controller.Stats;
        var breaksToday = stats.LastBreakDay == DateTime.Today.ToString("yyyy-MM-dd") ? stats.BreaksToday : 0;
        StatsText.Text = LocalizationManager.T("Tray_Stats_Format", breaksToday, stats.BreaksTotal);
    }

    private void OnBreakNow(object? sender, RoutedEventArgs e)
    {
        if (_scheduler.State is SchedulerState.Break) _scheduler.SkipBreak();
        else _scheduler.StartBreakNow();

        CloseMenu();
    }

    private void OnTogglePause(object? sender, RoutedEventArgs e)
    {
        _scheduler.TogglePause();
        UpdateState();
    }

    private void OnPausePreset(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string tag }) return;

        if (tag == "tomorrow") _scheduler.PauseUntilTomorrow();
        else if (int.TryParse(tag, out var minutes)) _scheduler.Pause(TimeSpan.FromMinutes(minutes));

        CloseMenu();
    }

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        _controller.ShowSettings();
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        _controller.Quit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            CloseMenu();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Closes the menu exactly once: focus loss also fires during closing itself,
    /// and a repeated Close from the Deactivated handler crashes WPF.
    /// </summary>
    private void CloseMenu()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scheduler.Ticked -= OnTick;
        base.OnClosed(e);
    }
}
