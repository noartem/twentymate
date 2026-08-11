using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TwentyMate.Core;

/// <summary>
/// Keeps the app's palette in sync with the system: the light/dark theme and Windows' accent
/// color are picked up automatically through Avalonia's platform settings, replacing manual
/// reads of the Personalize/DWM registry keys and <c>SystemEvents.UserPreferenceChanged</c>.
/// </summary>
public static class ThemeManager
{
    private static AppTheme _preference = AppTheme.System;

    public static bool IsDark { get; private set; } = true;

    public static Color Accent { get; private set; } = Color.FromRgb(0x4C, 0xC2, 0xFF);

    public static event EventHandler? Changed;

    public static void Initialize(AppTheme preference)
    {
        _preference = preference;

        if (Application.Current?.PlatformSettings is { } settings)
            settings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() => Apply(_preference));

        Apply(preference);
    }

    public static void Apply(AppTheme preference)
    {
        _preference = preference;

        var systemColors = Application.Current?.PlatformSettings?.GetColorValues();

        IsDark = preference switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => systemColors is not { ThemeVariant: PlatformThemeVariant.Light },
        };

        Accent = ReadAccent(systemColors);

        // FluentAvaloniaTheme's own controls (ToggleSwitch, Slider, ComboBox...) pick their
        // light/dark resources from this, independently of the custom brushes pushed below.
        if (Application.Current is { } app) app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        PushToResources();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Color ReadAccent(PlatformColorValues? systemColors) => systemColors switch
    {
        { } colors => IsDark ? Lighten(colors.AccentColor1, 0.25) : Darken(colors.AccentColor1, 0.1),
        null => IsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8),
    };

    /// <summary>Updates the app's resources so every window recolors without being recreated.</summary>
    private static void PushToResources()
    {
        if (Application.Current is not { } app) return;

        var resources = app.Resources;

        void Set(string key, Color color) => resources[key] = new SolidColorBrush(color);

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
