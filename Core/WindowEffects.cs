using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TwentyMate.Core;

public enum BackdropType
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4,
}

/// <summary>
/// Тонкая обёртка над DWM: материал подложки, скруглённые углы и тёмный режим рамки.
/// Всё это доступно только на Windows 11, на более старых системах вызовы просто игнорируются.
/// </summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    /// <summary>Windows 11 21H2 и новее — начиная с этой сборки работают Mica и скругления.</summary>
    public static bool IsWindows11 { get; } =
        Environment.OSVersion.Version is { Major: >= 10, Build: >= 22000 };

    /// <summary>Материал подложки Mica доступен как системный атрибут с 22H2.</summary>
    private static bool SupportsBackdropAttribute =>
        Environment.OSVersion.Version is { Major: >= 10, Build: >= 22621 };

    public static void Apply(Window window, BackdropType backdrop, bool dark, int cornerPreference = 2)
    {
        void Applier()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            SetDarkMode(handle, dark);
            SetCorners(handle, cornerPreference);

            if (backdrop != BackdropType.None) SetBackdrop(handle, window, backdrop);
        }

        if (window.IsLoaded) Applier();
        else window.SourceInitialized += (_, _) => Applier();
    }

    public static void SetDarkMode(IntPtr handle, bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    /// <summary>0 — по умолчанию, 1 — не скруглять, 2 — скруглить, 3 — малое скругление.</summary>
    public static void SetCorners(IntPtr handle, int preference)
    {
        if (!IsWindows11) return;
        var value = preference;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref value, sizeof(int));
    }

    private static void SetBackdrop(IntPtr handle, Window window, BackdropType backdrop)
    {
        if (!SupportsBackdropAttribute) return;

        // DWM рисует материал только там, где окно прозрачно, поэтому фон убираем
        // и расширяем рамку на всю клиентскую область.
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is { } target)
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;

        window.Background = System.Windows.Media.Brushes.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);

        var value = (int)backdrop;
        DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref value, sizeof(int));
    }
}
