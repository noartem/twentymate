using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace TwentyMate.Core;

public enum BackdropType
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4,
}

/// <summary>
/// Backdrop material goes through Avalonia's own <see cref="Window.TransparencyLevelHint"/>,
/// which does the DWM plumbing (extending the frame, clearing the composition background)
/// itself. Rounded corners and dark title bar mode have no Avalonia equivalent, so those two
/// remain thin DWM P/Invoke wrappers, same as before. All of this is only available on Windows
/// 11; on older systems the calls are simply ignored.
/// </summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11 21H2 and later — Mica and rounded corners work starting with this build.</summary>
    public static bool IsWindows11 { get; } =
        Environment.OSVersion.Version is { Major: >= 10, Build: >= 22000 };

    public static void Apply(Window window, BackdropType backdrop, bool dark, int cornerPreference = 2)
    {
        if (backdrop != BackdropType.None)
        {
            // Avalonia has no separate value for MicaAlt; Mica is the closest match and falls
            // back to Blur/None on its own where the OS doesn't support it.
            window.TransparencyLevelHint = backdrop is BackdropType.Acrylic
                ? [WindowTransparencyLevel.AcrylicBlur]
                : [WindowTransparencyLevel.Mica];
            window.Background = Brushes.Transparent;
        }

        void Applier()
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            SetDarkMode(handle, dark);
            SetCorners(handle, cornerPreference);
        }

        if (window.TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero) Applier();
        else window.Opened += (_, _) => Applier();
    }

    /// <summary>
    /// Skia defaults to grayscale antialiasing and lets baselines land on fractional pixels, which
    /// together read as noticeably softer than the ClearType-rendered text of native Windows apps.
    /// The attached value inherits down the visual tree, so one call per window covers its content.
    /// It has no XAML equivalent — <c>TextOptions</c> exposes no public attached-property field.
    /// </summary>
    public static void ApplyTextRendering(Window window) =>
        TextOptions.SetTextOptions(window, new TextOptions
        {
            TextRenderingMode = TextRenderingMode.SubpixelAntialias,
            BaselinePixelAlignment = BaselinePixelAlignment.Aligned,
        });

    public static void SetDarkMode(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    /// <summary>0 — default, 1 — no rounding, 2 — round, 3 — slight rounding.</summary>
    public static void SetCorners(IntPtr handle, int preference)
    {
        if (!IsWindows11) return;
        var value = preference;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref value, sizeof(int));
    }
}
