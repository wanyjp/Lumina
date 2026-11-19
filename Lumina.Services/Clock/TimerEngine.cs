using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Services.Clock;

/// <summary>
/// 倒计时引擎：以目标结束时刻为基准计算剩余时间，暂停时冻结剩余量，
/// 结束后发布一次带幂等 ID 的专注会话事件。定时器只负责触发刷新，不累加 tick。
/// </summary>
public sealed class TimerEngine : ITimerEngine, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private readonly Func<DateTimeOffset> _now;
    private readonly FocusSessionHub _sessions;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private readonly List<TimeSpan> _marks = [];

    private TimeSpan _target = TimeSpan.FromMinutes(25);
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private DateTimeOffset? _endAt;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private Guid? _sessionId;
    private bool _running;
    private bool _paused;
    private bool _disposed;

    public TimerEngine(FocusSessionHub sessions, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        _sessions = sessions;
        _now = now ?? (() => DateTimeOffset.Now);
        _timer = new System.Threading.Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<TimerState>? Changed;

    public TimerState State
    {
        get
        {
            lock (_gate)
            {
                return new TimerState(ComputeRemaining(), _running, _paused) { Target = _target };
            }
        }
    }

    public IReadOnlyList<TimeSpan> Marks
    {
        get
        {
            lock (_gate)
            {
                return _marks.ToArray();
            }
        }
    }

    public void Start(TimeSpan target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (target <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(target), "目标时长必须大于零。");
        }

        FocusSessionEvent? partial = null;
        lock (_gate)
        {
            // 运行中重新开始：先把未结束的会话按中途停止收尾，避免专注时长丢失。
            partial = BuildPartialLocked();

            _target = target;
            _remaining = target;
            _startedAt = _now();
            _endAt = _startedAt + target;
            _sessionId = Guid.NewGuid();
            _running = true;
            _paused = false;
            _pausedAt = null;
            _marks.Clear();
        }

        if (partial is not null)
        {
            _sessions.Publish(partial);
        }

        _timer.Change(TickInterval, TickInterval);
        RaiseChanged();
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (!_running || _paused)
            {
                return;
            }

            _remaining = ComputeRemaining();
            _endAt = null;
            _running = false;
            _paused = true;
            _pausedAt = _now();
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RaiseChanged();
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

            // 暂停时间不计入专注：开始时刻后移，结束事件不再跨入暂停区间。
            if (_pausedAt is { } pausedAt && _startedAt is { } startedAt)
            {
                var pausedFor = _now() - pausedAt;
                if (pausedFor > TimeSpan.Zero)
                {
                    _startedAt = startedAt + pausedFor;
                }
            }

            _pausedAt = null;
            _endAt = _now() + _remaining;
            _running = true;
            _paused = false;
        }

        _timer.Change(TickInterval, TickInterval);
        RaiseChanged();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        FocusSessionEvent? partial = null;
        lock (_gate)
        {
            partial = BuildPartialLocked();

            _remaining = _target;
            _endAt = null;
            _startedAt = null;
            _pausedAt = null;
            _sessionId = null;
            _running = false;
            _paused = false;
            _marks.Clear();
        }

        if (partial is not null)
        {
            _sessions.Publish(partial);
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RaiseChanged();
    }

    /// <summary>把正在进行的会话按“中途停止”收尾；没有可记录时长时返回 null。</summary>
    private FocusSessionEvent? BuildPartialLocked()
    {
        if (_sessionId is not { } sessionId || _startedAt is not { } startedAt)
        {
            return null;
        }

        var elapsed = _target - ComputeRemaining();
        if (elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return new FocusSessionEvent(
            sessionId,
            FocusSource.Timer,
            startedAt,
            _now(),
            elapsed,
            Completed: false);
    }

    public void Mark()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var recorded = false;
        lock (_gate)
        {
            if (!_running && !_paused)
            {
                return;
            }

            var elapsed = _target - ComputeRemaining();
            _marks.Add(elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
            recorded = true;
        }

        if (recorded)
        {
            RaiseChanged();
        }
    }

    private TimeSpan ComputeRemaining()
    {
        if (_running && _endAt is { } end)
        {
            var left = end - _now();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        return _remaining;
    }

    private void OnTick()
    {
        if (_disposed)
        {
            return;
        }

        FocusSessionEvent? finished = null;

        lock (_gate)
        {
            if (!_running || _endAt is null)
            {
                return;
            }

            if (_now() < _endAt)
            {
                RaiseChanged();
                return;
            }

            if (_sessionId is { } sessionId && _startedAt is { } startedAt)
            {
                finished = new FocusSessionEvent(
                    sessionId,
                    FocusSource.Timer,
                    startedAt,
                    _now(),
                    _target,
                    Completed: true);
            }

            _remaining = TimeSpan.Zero;
            _endAt = null;
            _sessionId = null;
            _startedAt = null;
            _running = false;
            _paused = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (finished is not null)
        {
            _sessions.Publish(finished);
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        TimerState state;
        lock (_gate)
        {
            state = new TimerState(ComputeRemaining(), _running, _paused) { Target = _target };
        }

        Changed?.Invoke(state);
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
