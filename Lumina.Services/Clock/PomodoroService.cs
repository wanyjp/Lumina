using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Services.Clock;

/// <summary>
/// 番茄钟：专注 → 休息循环，循环次数可配。阶段切换以结束时刻为基准，
/// 每个专注阶段结束时发布一次带幂等 ID 的会话事件；休息阶段不计入专注。
/// </summary>
public sealed class PomodoroService : IPomodoroService, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private readonly Func<DateTimeOffset> _now;
    private readonly FocusSessionHub _sessions;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    private TimeSpan _focus = TimeSpan.FromMinutes(25);
    private TimeSpan _rest = TimeSpan.FromMinutes(5);
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private DateTimeOffset? _endAt;
    private DateTimeOffset? _focusStartedAt;
    private DateTimeOffset? _pausedAt;
    private Guid? _sessionId;
    private int _cycle;
    private int _totalCycles;
    private bool _completed;
    private PomodoroPhase _phase = PomodoroPhase.Idle;
    private PomodoroPhase _previousPhase = PomodoroPhase.Focus;
    private bool _paused;
    private bool _disposed;

    public PomodoroService(FocusSessionHub sessions, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        _sessions = sessions;
        _now = now ?? (() => DateTimeOffset.Now);
        _timer = new System.Threading.Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<PomodoroState>? Updated;

    public PomodoroState Current
    {
        get
        {
            lock (_gate)
            {
                return new PomodoroState(
                    _phase,
                    ComputeRemaining(),
                    Math.Max(1, _cycle),
                    Math.Max(1, _totalCycles),
                    _completed);
            }
        }
    }

    public void Start(TimeSpan focus, TimeSpan rest, int cycles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (focus <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(focus), "专注时长必须大于零。");
        }

        if (rest < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(rest), "休息时长不能为负。");
        }

        if (cycles is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(cycles), "循环次数应在 1–12 之间。");
        }

        FocusSessionEvent? partial = null;

        lock (_gate)
        {
            // 运行中重新开始：先把未结束的专注按中途停止收尾，避免时长丢失。
            partial = BuildPartialLocked();

            _focus = focus;
            _rest = rest;
            _totalCycles = cycles;
            _cycle = 1;
            _completed = false;
            _paused = false;
            _pausedAt = null;
            _phase = PomodoroPhase.Focus;
            _remaining = focus;
            _focusStartedAt = _now();
            _sessionId = Guid.NewGuid();
            _endAt = _focusStartedAt + focus;
        }

        if (partial is not null)
        {
            _sessions.Publish(partial);
        }

        _timer.Change(TickInterval, TickInterval);
        RaiseUpdated();
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_phase is PomodoroPhase.Idle or PomodoroPhase.Paused || _endAt is null)
            {
                return;
            }

            _remaining = ComputeRemaining();
            _endAt = null;
            _paused = true;
            _pausedAt = _now();
            _previousPhase = _phase;
            _phase = PomodoroPhase.Paused;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RaiseUpdated();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (!_paused || _remaining <= TimeSpan.Zero)
            {
                return;
            }

            // 暂停时间不计入专注：开始时刻后移，跨午夜的归日也不会被暂停拖到第二天。
            if (_pausedAt is { } pausedAt && _focusStartedAt is { } startedAt)
            {
                var pausedFor = _now() - pausedAt;
                if (pausedFor > TimeSpan.Zero)
                {
                    _focusStartedAt = startedAt + pausedFor;
                }
            }

            _pausedAt = null;
            _phase = _previousPhase;
            _endAt = _now() + _remaining;
            _paused = false;
        }

        _timer.Change(TickInterval, TickInterval);
        RaiseUpdated();
    }

    public void ForceStop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        FocusSessionEvent? partial = null;

        lock (_gate)
        {
            partial = BuildPartialLocked();

            _phase = PomodoroPhase.Idle;
            _endAt = null;
            // 回到“待开始”时展示用户设定的专注时长，而不是 00:00:00；
            // 用户可直接再点开始进入新一轮。
            _remaining = _focus;
            _completed = false;
            _sessionId = null;
            _focusStartedAt = null;
            _pausedAt = null;
            _paused = false;
            _cycle = 0;
            _totalCycles = 0;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);

        if (partial is not null)
        {
            _sessions.Publish(partial);
        }

        RaiseUpdated();
    }

    private TimeSpan ComputeRemaining()
    {
        if (_endAt is { } end && _phase is PomodoroPhase.Focus or PomodoroPhase.Break)
        {
            var left = end - _now();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        return _remaining;
    }

    /// <summary>把正在进行的专注按“中途停止”收尾；休息阶段或没有时长时返回 null。</summary>
    private FocusSessionEvent? BuildPartialLocked()
    {
        var inFocus = _phase == PomodoroPhase.Focus
            || (_phase == PomodoroPhase.Paused && _previousPhase == PomodoroPhase.Focus);

        if (!inFocus || _sessionId is not { } sessionId || _focusStartedAt is not { } startedAt)
        {
            return null;
        }

        var elapsed = _focus - ComputeRemaining();
        if (elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return new FocusSessionEvent(
            sessionId,
            FocusSource.Pomodoro,
            startedAt,
            _now(),
            elapsed,
            Completed: false);
    }

    private void OnTick()
    {
        if (_disposed)
        {
            return;
        }

        FocusSessionEvent? finished = null;
        var restarted = false;

        lock (_gate)
        {
            if (_paused || _phase is PomodoroPhase.Idle || _endAt is null)
            {
                return;
            }

            if (_now() < _endAt)
            {
                RaiseUpdated();
                return;
            }

            if (_phase == PomodoroPhase.Focus)
            {
                if (_sessionId is { } sessionId && _focusStartedAt is { } startedAt)
                {
                    finished = new FocusSessionEvent(
                        sessionId,
                        FocusSource.Pomodoro,
                        startedAt,
                        _now(),
                        _focus,
                        Completed: true);
                }

                _sessionId = null;
                _focusStartedAt = null;

                if (_rest > TimeSpan.Zero && _cycle < _totalCycles)
                {
                    _phase = PomodoroPhase.Break;
                    _remaining = _rest;
                    _endAt = _now() + _rest;
                    _previousPhase = PomodoroPhase.Break;
                    restarted = true;
                }
                else
                {
                    // 最后一轮专注结束直接回到待开始，不再追加休息。
                    AdvanceCycle();
                }
            }
            else if (_phase == PomodoroPhase.Break)
            {
                AdvanceCycle();
            }

            if (!restarted && _phase == PomodoroPhase.Focus)
            {
                restarted = true;
            }
        }

        if (finished is not null)
        {
            _sessions.Publish(finished);
        }

        if (!restarted)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        RaiseUpdated();
    }

    /// <summary>休息结束后进入下一轮专注；达到循环数则回到待开始状态。调用方持有锁。</summary>
    private void AdvanceCycle()
    {
        if (_cycle >= _totalCycles)
        {
            _phase = PomodoroPhase.Idle;
            // 全部轮次完成后显示用户设定的专注时长，方便直接开始新一轮。
            _remaining = _focus;
            _endAt = null;
            _completed = true;
            return;
        }

        _cycle++;
        _phase = PomodoroPhase.Focus;
        _remaining = _focus;
        _focusStartedAt = _now();
        _sessionId = Guid.NewGuid();
        _previousPhase = PomodoroPhase.Focus;
        _endAt = _focusStartedAt + _focus;
    }

    private void RaiseUpdated()
    {
        PomodoroState state;
        lock (_gate)
        {
            state = new PomodoroState(
                _phase,
                ComputeRemaining(),
                Math.Max(1, _cycle),
                Math.Max(1, _totalCycles),
                _completed);
        }

        Updated?.Invoke(state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }
}
