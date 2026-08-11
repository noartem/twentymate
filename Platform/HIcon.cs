using System;
using System.Runtime.InteropServices;

namespace TwentyMate.Platform;

/// <summary>Builds a Win32 HICON from a 32bpp BGRA pixel buffer, for use with Shell_NotifyIcon.</summary>
internal static class HIcon
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool IsIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    /// <summary>
    /// Builds an HICON from top-down 32bpp BGRA pixels. The caller owns the returned handle and
    /// must release it with <see cref="Destroy"/> once it's no longer in use.
    /// </summary>
    public static IntPtr Create(byte[] bgra, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // negative: top-down, matching the source buffer's row order
            Planes = 1,
            BitCount = 32,
            Compression = 0, // BI_RGB
        };

        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed.");

        Marshal.Copy(bgra, 0, bits, bgra.Length);

        // An all-zero 1bpp AND mask: the color bitmap already carries per-pixel alpha, and an
        // empty mask tells Windows to trust it instead of punching holes from the mask.
        var maskStride = ((width + 15) / 16) * 2;
        var maskBits = Marshal.AllocHGlobal(maskStride * height);
        var mask = IntPtr.Zero;
        try
        {
            for (var i = 0; i < maskStride * height; i++) Marshal.WriteByte(maskBits, i, 0);
            mask = CreateBitmap(width, height, 1, 1, maskBits);
        }
        finally
        {
            Marshal.FreeHGlobal(maskBits);
        }

        var info = new IconInfo { IsIcon = true, ColorBitmap = color, MaskBitmap = mask };
        var icon = CreateIconIndirect(ref info);

        DeleteObject(color);
        DeleteObject(mask);

        if (icon == IntPtr.Zero) throw new InvalidOperationException("CreateIconIndirect failed.");
        return icon;
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }
}
