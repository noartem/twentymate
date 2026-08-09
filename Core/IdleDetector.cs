using System;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Определяет простой пользователя через системное время последнего ввода
/// с клавиатуры или мыши.
/// </summary>
public static class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>Сколько времени прошло с последнего движения мыши или нажатия клавиши.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Оба значения — это тики GetTickCount, вычитание в uint корректно
        // переживает переполнение примерно раз в 49.7 дня.
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
    }
}
