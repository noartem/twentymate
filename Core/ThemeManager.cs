using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TwentyMate.Core;

/// <summary>
/// Держит палитру приложения в согласии с системой: светлая/тёмная тема
/// и акцентный цвет Windows подхватываются автоматически.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _preference = AppTheme.System;

    public static bool IsDark { get; private set; } = true;

    public static Color Accent { get; private set; } = Color.FromRgb(0x4C, 0xC2, 0xFF);

    public static event EventHandler? Changed;

    public static void Initialize(AppTheme preference)
    {
        _preference = preference;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_preference));
        };

        Apply(preference);
    }

    public static void Apply(AppTheme preference)
    {
        _preference = preference;

        IsDark = preference switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsSystemDark(),
        };

        Accent = ReadSystemAccent();
        PushToResources();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // 0 — тёмная тема приложений, 1 — светлая.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true;
        }
    }

    private static Color ReadSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                // В реестре цвет лежит как AABBGGRR.
                var bytes = BitConverter.GetBytes(raw);
                var color = Color.FromRgb(bytes[0], bytes[1], bytes[2]);
                return IsDark ? Lighten(color, 0.25) : Darken(color, 0.1);
            }
        }
        catch
        {
            // Не критично — останется цвет по умолчанию.
        }

        return IsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
    }

    /// <summary>Обновляет ресурсы приложения, чтобы все окна перекрасились без пересоздания.</summary>
    private static void PushToResources()
    {
        if (Application.Current is not { } app) return;

        var resources = app.Resources;

        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        void SetColor(string key, Color color) => resources[key] = color;

        SetColor("AccentColor", Accent);
        Set("AccentBrush", Accent);
        Set("AccentHoverBrush", IsDark ? Lighten(Accent, 0.12) : Lighten(Accent, 0.1));
        Set("AccentPressedBrush", IsDark ? Darken(Accent, 0.15) : Darken(Accent, 0.2));
        Set("AccentSoftBrush", WithAlpha(Accent, IsDark ? 0.20 : 0.14));

        if (IsDark)
        {
            Set("WindowBackgroundBrush", Color.FromRgb(0x20, 0x20, 0x20));
            Set("LayerBrush", Color.FromArgb(0x4D, 0x3A, 0x3A, 0x3A));
            Set("LayerStrongBrush", Color.FromArgb(0x80, 0x2D, 0x2D, 0x2D));
            Set("ControlBrush", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
            Set("ControlHoverBrush", Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));
            Set("ControlPressedBrush", Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
            Set("StrokeBrush", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            Set("StrokeStrongBrush", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            Set("TextPrimaryBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set("TextSecondaryBrush", Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF));
            Set("TextTertiaryBrush", Color.FromArgb(0x8B, 0xFF, 0xFF, 0xFF));
            Set("TextOnAccentBrush", Color.FromRgb(0x00, 0x00, 0x00));
            Set("TrackBrush", Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            Set("WindowBackgroundBrush", Color.FromRgb(0xF3, 0xF3, 0xF3));
            Set("LayerBrush", Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            Set("LayerStrongBrush", Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            Set("ControlBrush", Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            Set("ControlHoverBrush", Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            Set("ControlPressedBrush", Color.FromArgb(0x80, 0xF9, 0xF9, 0xF9));
            Set("StrokeBrush", Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
            Set("StrokeStrongBrush", Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
            Set("TextPrimaryBrush", Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set("TextSecondaryBrush", Color.FromArgb(0xC5, 0x00, 0x00, 0x00));
            Set("TextTertiaryBrush", Color.FromArgb(0x9E, 0x00, 0x00, 0x00));
            Set("TextOnAccentBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set("TrackBrush", Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        }
    }

    public static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R + 255 * amount, 0, 255),
        (byte)Math.Clamp(color.G + 255 * amount, 0, 255),
        (byte)Math.Clamp(color.B + 255 * amount, 0, 255));

    public static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.B * (1 - amount), 0, 255));

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
}
