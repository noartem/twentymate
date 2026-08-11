using System;
using Avalonia.Threading;

namespace TwentyMate.Core;

public enum SchedulerState
{
    /// <summary>Working, the timer is ticking down to the next break.</summary>
    Working,

    /// <summary>A break is in progress.</summary>
    Break,

    /// <summary>The user has paused the timer.</summary>
    Paused,

    /// <summary>Outside working hours — the timer is asleep.</summary>
    OffHours,
}

/// <summary>
/// The heart of the app: counts down to a break, runs the break itself,
/// and knows how to sleep outside working hours.
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

    /// <summary>How much time is left until the end of the current phase.</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary>Progress of the current phase from 0 to 1.</summary>
    public double Progress => _phaseLength > TimeSpan.Zero
        ? Math.Clamp(1 - Remaining.TotalSeconds / _phaseLength.TotalSeconds, 0, 1)
        : 0;

    /// <summary>When the pause will end on its own (null — the pause is indefinite).</summary>
    public DateTime? PausedUntil => _pausedUntil;

    /// <summary>The pause was set automatically due to idleness, not manually by the user.</summary>
    public bool IsPausedByIdle => _pausedByIdle;

    public event EventHandler? Ticked;
    public event EventHandler? StateChanged;
    public event EventHandler? BreakStarted;

    /// <summary>The break has finished. Argument: true — ran to completion, false — skipped.</summary>
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

        // Changing the interval while working rebuilds the current countdown,
        // so the new value takes effect right away rather than on the next cycle.
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

    /// <summary>End the current break early.</summary>
    public void SkipBreak()
    {
        if (State is not SchedulerState.Break) return;

        BeginWorkPhase();
        BreakFinished?.Invoke(this, false);
    }

    /// <summary>Postpone the break without starting it.</summary>
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

    /// <summary>Pause until a separate call to <see cref="Resume"/> or until the given moment.</summary>
    public void Pause(TimeSpan? duration = null)
    {
        _pausedByIdle = false;
        _pausedUntil = duration.HasValue ? DateTime.Now + duration.Value : null;
        Remaining = duration ?? TimeSpan.Zero;
        _phaseLength = duration ?? TimeSpan.Zero;
        _phaseEndsAt = _pausedUntil ?? DateTime.MaxValue;
        SetState(SchedulerState.Paused);
    }

    /// <summary>Pause until the start of the next day.</summary>
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
