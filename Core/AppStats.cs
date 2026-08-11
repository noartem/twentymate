using System;
using System.IO;
using System.Text.Json;

namespace TwentyMate.Core;

/// <summary>
/// Usage history — separate from <see cref="AppSettings"/> so resetting settings to
/// defaults doesn't wipe it, and so it isn't mixed in with what the user actually configures.
/// </summary>
public sealed class AppStats
{
    public bool FirstRunDone { get; set; }
    public int BreaksToday { get; set; }
    public int BreaksTotal { get; set; }
    public string LastBreakDay { get; set; } = "";
}

public static class StatsStore
{
    public static string FilePath { get; } = Path.Combine(SettingsStore.Directory, "stats.json");

    public static AppStats Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var stats = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppStats);
                if (stats is not null) return stats;
            }
        }
        catch
        {
            // A corrupted stats file shouldn't block startup — fall back to defaults.
        }

        return MigrateFromLegacySettings() ?? new AppStats();
    }

    /// <summary>
    /// Older versions kept these fields inside settings.json itself. Picks them up once, on
    /// the first run after upgrading, so existing counters and the first-run flag survive.
    /// </summary>
    private static AppStats? MigrateFromLegacySettings()
    {
        try
        {
            if (!File.Exists(SettingsStore.FilePath)) return null;

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsStore.FilePath));
            var root = document.RootElement;
            if (!root.TryGetProperty("BreaksTotal", out _) && !root.TryGetProperty("FirstRunDone", out _))
                return null;

            var stats = new AppStats
            {
                FirstRunDone = root.TryGetProperty("FirstRunDone", out var firstRun) && firstRun.GetBoolean(),
                BreaksToday = root.TryGetProperty("BreaksToday", out var today) ? today.GetInt32() : 0,
                BreaksTotal = root.TryGetProperty("BreaksTotal", out var total) ? total.GetInt32() : 0,
                LastBreakDay = root.TryGetProperty("LastBreakDay", out var lastDay) ? lastDay.GetString() ?? "" : "",
            };
            Save(stats);
            return stats;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppStats stats)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(stats, SettingsJsonContext.Default.AppStats));
        }
        catch
        {
            // No write permission — keep working with the in-memory stats.
        }
    }
}
