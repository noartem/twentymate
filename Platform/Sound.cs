using System;
using System.Runtime.InteropServices;

namespace TwentyMate.Platform;

/// <summary>
/// Plays an in-memory WAV via winmm — <c>System.Media.SoundPlayer</c> depends on
/// System.Windows.Extensions and isn't trim/AOT-safe.
/// </summary>
internal static class Sound
{
    private const uint SndMemory = 0x0004;
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    /// <summary>
    /// Copies a WAV payload to unmanaged memory that lives for the rest of the process. With
    /// SND_ASYNC | SND_MEMORY, winmm keeps reading from the pointer while it plays, so the
    /// buffer can't be a movable, GC-collectible managed array — pin once, reuse forever.
    /// </summary>
    public static IntPtr Pin(byte[] wav)
    {
        var pointer = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, pointer, wav.Length);
        return pointer;
    }

    public static void Play(IntPtr pinnedWav)
    {
        try
        {
            PlaySound(pinnedWav, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
        }
        catch
        {
            // No audio device — silence isn't a reason to crash.
        }
    }
}
