using System;
using System.Runtime.InteropServices;

namespace TwentyMate.Core;

/// <summary>
/// Detects user idle time via the system's last-input time from the
/// keyboard or mouse.
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

    /// <summary>How much time has passed since the last mouse move or key press.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both values are GetTickCount ticks; subtracting as uint correctly
        // survives the rollover that happens roughly every 49.7 days.
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
    }
}
