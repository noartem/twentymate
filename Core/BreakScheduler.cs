using System;
using System.Windows.Threading;

namespace TwentyMate.Core;

public enum SchedulerState
{
    /// <summary>Идёт работа, тикает таймер до следующего перерыва.</summary>
    Working,

    /// <summary>Идёт перерыв.</summary>
    Break,

    /// <summary>Пользователь поставил таймер на паузу.</summary>
    Paused,

    /// <summary>Вне рабочих часов — таймер спит.</summary>
    OffHours,
}

/// <summary>
/// Сердце приложения: отсчитывает время до перерыва, ведёт сам перерыв
/// и умеет засыпать вне рабочих часов.
/// </summary>
public sealed class BreakScheduler
{
    private readonly DispatcherTimer _timer;
    private AppSettings _settings;

    private DateTime _phaseEndsAt;
    private TimeSpan _phaseLength;
    private DateTime? _pausedUntil;
    private bool _pausedByIdle;

    public BreakScheduler(AppSettings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _timer.Tick += (_, _) => Update();
    }

    public SchedulerState State { get; private set; } = SchedulerState.Working;

    /// <summary>Сколько осталось до конца текущей фазы.</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary>Прогресс текущей фазы от 0 до 1.</summary>
    public double Progress => _phaseLength > TimeSpan.Zero
        ? Math.Clamp(1 - Remaining.TotalSeconds / _phaseLength.TotalSeconds, 0, 1)
        : 0;

    /// <summary>Когда пауза закончится сама (null — пауза бессрочная).</summary>
    public DateTime? PausedUntil => _pausedUntil;

    /// <summary>Пауза поставлена автоматически из-за простоя, а не вручную пользователем.</summary>
    public bool IsPausedByIdle => _pausedByIdle;

    public event EventHandler? Ticked;
    public event EventHandler? StateChanged;
    public event EventHandler? BreakStarted;

    /// <summary>Перерыв завершён. Аргумент: true — досижен до конца, false — пропущен.</summary>
    public event EventHandler<bool>? BreakFinished;

    public void Start()
    {
        BeginWorkPhase();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void ApplySettings(AppSettings settings)
    {
        var oldInterval = _settings.IntervalMinutes;
        _settings = settings;

        // Изменение интервала во время работы пересобирает текущий отсчёт,
        // чтобы новое значение вступало в силу сразу, а не со следующего цикла.
        if (State is SchedulerState.Working && oldInterval != settings.IntervalMinutes)
            BeginWorkPhase();
    }

    public void StartBreakNow()
    {
        if (State is SchedulerState.Break) return;

        _pausedUntil = null;
        _phaseLength = TimeSpan.FromSeconds(_settings.BreakSeconds);
        _phaseEndsAt = DateTime.Now + _phaseLength;
        Remaining = _phaseLength;
        SetState(SchedulerState.Break);
        BreakStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Завершить текущий перерыв досрочно.</summary>
    public void SkipBreak()
    {
        if (State is not SchedulerState.Break) return;

        BeginWorkPhase();
        BreakFinished?.Invoke(this, false);
    }

    /// <summary>Отложить перерыв, не начиная его.</summary>
    public void Postpone(TimeSpan by)
    {
        if (State is SchedulerState.Break)
        {
            BreakFinished?.Invoke(this, false);
            SetState(SchedulerState.Working);
        }

        _pausedUntil = null;
        _phaseLength = by;
        _phaseEndsAt = DateTime.Now + by;
        Remaining = by;
        SetState(SchedulerState.Working);
    }

    /// <summary>Пауза до отдельного вызова <see cref="Resume"/> или до указанного момента.</summary>
    public void Pause(TimeSpan? duration = null)
    {
        _pausedByIdle = false;
        _pausedUntil = duration.HasValue ? DateTime.Now + duration.Value : null;
        Remaining = duration ?? TimeSpan.Zero;
        _phaseLength = duration ?? TimeSpan.Zero;
        _phaseEndsAt = _pausedUntil ?? DateTime.MaxValue;
        SetState(SchedulerState.Paused);
    }

    /// <summary>Пауза до начала следующего дня.</summary>
    public void PauseUntilTomorrow() => Pause(DateTime.Today.AddDays(1) - DateTime.Now);

    public void Resume()
    {
        _pausedByIdle = false;
        _pausedUntil = null;
        BeginWorkPhase();
    }

    public void TogglePause()
    {
        if (State is SchedulerState.Paused) Resume();
        else Pause();
    }

    private void BeginWorkPhase()
    {
        _phaseLength = TimeSpan.FromMinutes(_settings.IntervalMinutes);
        _phaseEndsAt = DateTime.Now + _phaseLength;
        Remaining = _phaseLength;
        SetState(SchedulerState.Working);
    }

    private void Update()
    {
        var now = DateTime.Now;

        switch (State)
        {
            case SchedulerState.Paused:
                if (_pausedByIdle)
                {
                    if (!_settings.AutoPauseOnIdle ||
                        IdleDetector.GetIdleTime() < TimeSpan.FromMinutes(_settings.IdleThresholdMinutes))
                        Resume();
                    break;
                }

                if (_pausedUntil is { } until)
                {
                    Remaining = until - now;
                    if (Remaining <= TimeSpan.Zero) Resume();
                }
                break;

            case SchedulerState.OffHours:
                if (_settings.IsWithinWorkingHours(now)) BeginWorkPhase();
                break;

            case SchedulerState.Working:
                if (!_settings.IsWithinWorkingHours(now))
                {
                    Remaining = TimeSpan.Zero;
                    SetState(SchedulerState.OffHours);
                    break;
                }

                if (_settings.AutoPauseOnIdle &&
                    IdleDetector.GetIdleTime() >= TimeSpan.FromMinutes(_settings.IdleThresholdMinutes))
                {
                    _pausedByIdle = true;
                    _pausedUntil = null;
                    Remaining = TimeSpan.Zero;
                    SetState(SchedulerState.Paused);
                    break;
                }

                Remaining = _phaseEndsAt - now;
                if (Remaining <= TimeSpan.Zero) StartBreakNow();
                break;

            case SchedulerState.Break:
                Remaining = _phaseEndsAt - now;
                if (Remaining <= TimeSpan.Zero)
                {
                    BeginWorkPhase();
                    BreakFinished?.Invoke(this, true);
                }
                break;
        }

        if (Remaining < TimeSpan.Zero) Remaining = TimeSpan.Zero;
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(SchedulerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
