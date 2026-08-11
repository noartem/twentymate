using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using TwentyMate.Core;

namespace TwentyMate.Views;

/// <summary>
/// The fullscreen break window. Additional monitors show the same overlay,
/// but without buttons — controls live on the primary screen.
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

    private Screen? _targetScreen;
    private bool _closing;

    public BreakWindow(AppSettings settings, BreakScheduler scheduler, bool showControls)
    {
        _settings = settings;
        _scheduler = scheduler;
        _showControls = showControls;

        InitializeComponent();

        WindowEffects.ApplyTextRendering(this);

        _scheduler.Ticked += OnTick;

        PostponeButton.Content = LocalizationManager.T("Break_PostponeFormatted", settings.PostponeMinutes);

        if (!showControls)
        {
            Actions.IsVisible = false;
            HintText.IsVisible = false;
        }
        else if (!settings.AllowSkip)
        {
            SkipButton.IsVisible = false;
            PostponeButton.IsVisible = false;
            HintText.Text = LocalizationManager.T("Break_AutoEndHint");
        }

        UpdateCountdown();
    }

    public void ShowOn(Screen screen)
    {
        _targetScreen = screen;
        Show();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Position in physical pixels: with mixed monitor scaling, Avalonia's DIP-based
        // Width/Height/Position can miss the intended screen — see the same reasoning on
        // TrayMenuWindow.PositionNearCursor.
        if (_targetScreen is { } screen)
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                var bounds = screen.Bounds;
                SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    SwpNoActivate | SwpShowWindow);
            }
        }

        PlayEntrance();
        if (_showControls) Activate();
    }

    private void PlayEntrance()
    {
        var duration = TimeSpan.FromMilliseconds(320);
        var easing = new CubicEaseOut();

        var opacity = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        _ = opacity.RunAsync(this);

        // WPF-style ScaleTransform objects can't be driven by Animation.RunAsync (it requires
        // a Visual) or by a Transitions entry — only the CSS-like RenderTransform string form
        // can be transitioned. ContentRoot.axaml declares a TransformOperationsTransition on
        // RenderTransform, so setting the end value here is enough to animate to it.
        ContentRoot.RenderTransform = TransformOperations.Parse("scale(1)");
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

        // The ring depletes as the break progresses — visually "time draining away".
        Ring.Value = 1 - _scheduler.Progress;
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => _scheduler.SkipBreak();

    private void OnPostpone(object? sender, RoutedEventArgs e) =>
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

    /// <summary>Smoothly fades out the window and closes it. Repeated calls are safe.</summary>
    public async void CloseOverlay()
    {
        if (_closing) return;
        _closing = true;

        _scheduler.Ticked -= OnTick;

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, Opacity) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        await fade.RunAsync(this);

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scheduler.Ticked -= OnTick;
        base.OnClosed(e);
    }
}
