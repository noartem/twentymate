using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using TwentyMate.Core;
using Forms = System.Windows.Forms;

namespace TwentyMate.Views;

/// <summary>
/// Всплывающее меню значка в трее — свой вариант вместо системного
/// контекстного меню, чтобы вписаться в оформление Windows 11.
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

        // Размеры известны только после Show при SizeToContent.
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

        // ActualWidth/Height логические — переводим в пиксели этого монитора.
        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;

        var width = (int)Math.Ceiling(ActualWidth * scaleX);
        var height = (int)Math.Ceiling(ActualHeight * scaleY);

        // Прижимаем к тому углу рабочей области, где стоит панель задач.
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
                StatusText.Text = "перерыв идёт";
                RingGlyph.Text = "";
                Ring.Value = 1 - _scheduler.Progress;
                BreakNowText.Text = "Пропустить перерыв";
                break;

            case SchedulerState.Paused:
                TimeText.Text = _scheduler.PausedUntil is { } until ? $"до {until:HH:mm}" : "На паузе";
                StatusText.Text = "напоминания отключены";
                RingGlyph.Text = "";
                Ring.Value = 0;
                BreakNowText.Text = "Перерыв сейчас";
                break;

            case SchedulerState.OffHours:
                TimeText.Text = "Не сейчас";
                StatusText.Text = $"рабочие часы {_settings.WorkStart}–{_settings.WorkEnd}";
                RingGlyph.Text = "";
                Ring.Value = 0;
                BreakNowText.Text = "Перерыв сейчас";
                break;

            default:
                TimeText.Text = remaining;
                StatusText.Text = "до следующего перерыва";
                RingGlyph.Text = "";
                Ring.Value = _scheduler.Progress;
                BreakNowText.Text = "Перерыв сейчас";
                break;
        }

        var paused = _scheduler.State is SchedulerState.Paused;
        PauseText.Text = paused ? "Продолжить" : "Пауза";
        PauseGlyph.Text = paused ? "" : "";
        PausePresets.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;

        StatsText.Text = _settings.LastBreakDay == DateTime.Today.ToString("yyyy-MM-dd")
            ? $"Сегодня перерывов: {_settings.BreaksToday}   ·   всего: {_settings.BreaksTotal}"
            : $"Сегодня перерывов: 0   ·   всего: {_settings.BreaksTotal}";
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
    /// Закрывает меню ровно один раз: потеря фокуса приходит и во время самого закрытия,
    /// а повторный Close из обработчика Deactivated роняет WPF.
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
