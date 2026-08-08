using System;
using System.IO;
using System.Media;

namespace TwentyMate.Core;

/// <summary>
/// Мягкие сигналы начала и конца перерыва. Звуки синтезируются в память,
/// поэтому приложению не нужны внешние аудиофайлы.
/// </summary>
public static class SoundService
{
    private const int SampleRate = 44100;

    private static readonly Lazy<byte[]> StartTone = new(() => BuildChime([587.33, 880.00], 0.42));
    private static readonly Lazy<byte[]> EndTone = new(() => BuildChime([880.00, 587.33], 0.36));

    public static void PlayBreakStart() => Play(StartTone.Value);

    public static void PlayBreakEnd() => Play(EndTone.Value);

    private static void Play(byte[] wav)
    {
        try
        {
            var player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
        }
        catch
        {
            // Нет звукового устройства — тишина не повод падать.
        }
    }

    /// <summary>Двухнотный перезвон с плавным затуханием, чтобы не пугать резким писком.</summary>
    private static byte[] BuildChime(double[] notes, double noteSeconds)
    {
        var perNote = (int)(SampleRate * noteSeconds);
        var total = perNote * notes.Length;

        var samples = new short[total];
        for (var n = 0; n < notes.Length; n++)
        {
            var frequency = notes[n];
            for (var i = 0; i < perNote; i++)
            {
                var t = (double)i / SampleRate;
                var position = (double)i / perNote;

                // Быстрая атака и экспоненциальный спад — звук воспринимается как мягкий колокольчик.
                var attack = Math.Min(1, position / 0.02);
                var decay = Math.Exp(-4.5 * position);
                var envelope = attack * decay;

                // Небольшая примесь октавы делает тембр менее «электронным».
                var wave = Math.Sin(2 * Math.PI * frequency * t)
                           + 0.3 * Math.Sin(4 * Math.PI * frequency * t);

                samples[n * perNote + i] = (short)(wave * envelope * 0.28 * short.MaxValue);
            }
        }

        return EncodeWav(samples);
    }

    private static byte[] EncodeWav(short[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var dataBytes = samples.Length * sizeof(short);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                        // размер блока fmt
        writer.Write((short)1);                  // PCM
        writer.Write((short)1);                  // моно
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);            // байт в секунду
        writer.Write((short)2);                  // выравнивание блока
        writer.Write((short)16);                 // бит на сэмпл
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        foreach (var sample in samples) writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}
