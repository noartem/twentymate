using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TwentyMate.Core;
using Forms = System.Windows.Forms;

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

        _scheduler.Ticked += OnTick;
        Deactivated += (_, _) => CloseMenu();

        UpdateState();
    }

    public void ShowNearTray()
    {
        Show();

        // Dimensions are only known after Show with SizeToContent.
        Dispatcher.BeginInvoke(new Action(PositionNearCursor),
            System.Windows.Threading.DispatcherPriority.Loaded);

        Activate();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void PositionNearCursor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;

        // ActualWidth/Height are logical — convert to this monitor's pixels.
        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;

        var width = (int)Math.Ceiling(ActualWidth * scaleX);
        var height = (int)Math.Ceiling(ActualHeight * scaleY);

        // Snap to whichever corner of the working area the taskbar is on.
        var x = Math.Clamp(cursor.X - width / 2, work.Left, work.Right - width);
        var y = cursor.Y > work.Top + work.Height / 2
            ? work.Bottom - height
            : work.Top;

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
                RingGlyph.Text = "";
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
                RingGlyph.Text = "";
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
        PausePresets.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;

        var breaksToday = _settings.LastBreakDay == DateTime.Today.ToString("yyyy-MM-dd") ? _settings.BreaksToday : 0;
        StatsText.Text = LocalizationManager.T("Tray_Stats_Format", breaksToday, _settings.BreaksTotal);
    }

    private void OnBreakNow(object sender, RoutedEventArgs e)
    {
        if (_scheduler.State is SchedulerState.Break) _scheduler.SkipBreak();
        else _scheduler.StartBreakNow();

        CloseMenu();
    }

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _scheduler.TogglePause();
        UpdateState();
    }

    private void OnPausePreset(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        if (tag == "tomorrow") _scheduler.PauseUntilTomorrow();
        else if (int.TryParse(tag, out var minutes)) _scheduler.Pause(TimeSpan.FromMinutes(minutes));

        CloseMenu();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        _controller.ShowSettings();
    }

    private void OnQuit(object sender, RoutedEventArgs e)
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
