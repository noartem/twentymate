using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwentyMate.Core;

public enum NotificationStyle
{
    /// <summary>Меняется только значок в трее — самый ненавязчивый режим.</summary>
    TrayIconOnly = 0,

    /// <summary>Системное уведомление Windows.</summary>
    SystemToast = 1,

    /// <summary>Полноэкранное окно перерыва с таймером.</summary>
    Overlay = 2,
}

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed class AppSettings
{
    // --- Расписание ---
    public int IntervalMinutes { get; set; } = 20;
    public int BreakSeconds { get; set; } = 20;
    public int PostponeMinutes { get; set; } = 5;

    // --- Уведомления ---
    public NotificationStyle Style { get; set; } = NotificationStyle.Overlay;
    public bool SoundOnBreakStart { get; set; } = true;
    public bool SoundOnBreakEnd { get; set; } = true;
    public bool AllowSkip { get; set; } = true;
    public bool DimAllScreens { get; set; } = true;

    // --- Рабочие часы ---
    public bool WorkingHoursEnabled { get; set; }
    public string WorkStart { get; set; } = "09:00";
    public string WorkEnd { get; set; } = "18:00";

    /// <summary>Индексы соответствуют <see cref="DayOfWeek"/>: 0 — воскресенье.</summary>
    public bool[] WorkDays { get; set; } = [false, true, true, true, true, true, false];

    // --- Автопауза ---
    public bool AutoPauseOnIdle { get; set; }
    public int IdleThresholdMinutes { get; set; } = 5;

    // --- Общее ---
    public bool LaunchAtLogin { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool FirstRunDone { get; set; }

    // --- Статистика ---
    public int BreaksToday { get; set; }
    public int BreaksTotal { get; set; }
    public string LastBreakDay { get; set; } = "";

    [JsonIgnore]
    public TimeSpan WorkStartTime => ParseTime(WorkStart, new TimeSpan(9, 0, 0));

    [JsonIgnore]
    public TimeSpan WorkEndTime => ParseTime(WorkEnd, new TimeSpan(18, 0, 0));

    private static TimeSpan ParseTime(string value, TimeSpan fallback) =>
        TimeSpan.TryParse(value, out var parsed) ? parsed : fallback;

    /// <summary>Приводит значения к допустимым диапазонам после загрузки с диска.</summary>
    public void Normalize()
    {
        // Границы совпадают с диапазонами ползунков в окне настроек.
        IntervalMinutes = Math.Clamp(IntervalMinutes, 5, 120);
        BreakSeconds = Math.Clamp(BreakSeconds, 5, 600);
        PostponeMinutes = Math.Clamp(PostponeMinutes, 1, 60);
        IdleThresholdMinutes = Math.Clamp(IdleThresholdMinutes, 1, 60);

        if (WorkDays is not { Length: 7 })
            WorkDays = [false, true, true, true, true, true, false];

        if (!Enum.IsDefined(Style)) Style = NotificationStyle.Overlay;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
    }

    /// <summary>Попадает ли момент в рабочее время (при выключенном расписании — всегда да).</summary>
    public bool IsWithinWorkingHours(DateTime moment)
    {
        if (!WorkingHoursEnabled) return true;
        if (!WorkDays[(int)moment.DayOfWeek]) return false;

        var time = moment.TimeOfDay;
        var start = WorkStartTime;
        var end = WorkEndTime;

        // Интервал через полночь, например 22:00 — 06:00.
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TwentyMate");

    public static string FilePath { get; } = Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch
        {
            // Повреждённый файл настроек не должен мешать запуску — откатываемся к значениям по умолчанию.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Нет прав на запись — работаем дальше с настройками в памяти.
        }
    }
}
