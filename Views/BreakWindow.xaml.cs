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
/// Полноэкранное окно перерыва. На дополнительных мониторах показывается
/// та же заставка, но без кнопок — управление живёт на основном экране.
/// </summary>
public partial class BreakWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    private readonly AppSettings _settings;
    private readonly BreakScheduler _scheduler;
    private readonly bool _showControls;

    private Forms.Screen? _targetScreen;
    private bool _closing;

    public BreakWindow(AppSettings settings, BreakScheduler scheduler, bool showControls)
    {
        _settings = settings;
        _scheduler = scheduler;
        _showControls = showControls;

        InitializeComponent();

        _scheduler.Ticked += OnTick;

        PostponeButton.Content = $"Отложить на {settings.PostponeMinutes} мин";

        if (!showControls)
        {
            Actions.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Collapsed;
        }
        else if (!settings.AllowSkip)
        {
            SkipButton.Visibility = Visibility.Collapsed;
            PostponeButton.Visibility = Visibility.Collapsed;
            HintText.Text = "Перерыв закончится автоматически";
        }

        UpdateCountdown();
    }

    public void ShowOn(Forms.Screen screen)
    {
        _targetScreen = screen;
        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Позиционируем в физических пикселях: при разном масштабе мониторов
        // логические координаты WPF промахиваются мимо нужного экрана.
        if (_targetScreen is { } screen)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var bounds = screen.Bounds;
            SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SwpNoActivate | SwpShowWindow);
        }

        PlayEntrance();
        if (_showControls) Activate();
    }

    private void PlayEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        ContentScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, duration) { EasingFunction = ease });
        ContentScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, duration) { EasingFunction = ease });
    }

    private void OnTick(object? sender, EventArgs e) => UpdateCountdown();

    private void UpdateCountdown()
    {
        if (_scheduler.State is not SchedulerState.Break)
        {
            CloseOverlay();
            return;
        }

        var remaining = _scheduler.Remaining;
        CountdownText.Text = remaining.TotalSeconds >= 60
            ? TrayController.FormatClock(remaining)
            : Math.Ceiling(remaining.TotalSeconds).ToString("0");

        // Кольцо убывает по мере перерыва — визуально «время утекает».
        Ring.Value = 1 - _scheduler.Progress;
    }

    private void OnSkip(object sender, RoutedEventArgs e) => _scheduler.SkipBreak();

    private void OnPostpone(object sender, RoutedEventArgs e) =>
        _scheduler.Postpone(TimeSpan.FromMinutes(_settings.PostponeMinutes));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape && _showControls && _settings.AllowSkip)
        {
            _scheduler.SkipBreak();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Плавно гасит окно и закрывает его. Повторные вызовы безопасны.</summary>
    public void CloseOverlay()
    {
        if (_closing) return;
        _closing = true;

        _scheduler.Ticked -= OnTick;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _scheduler.Ticked -= OnTick;
        base.OnClosed(e);
    }
}
