using Lumina.Services.Contracts;

namespace Lumina.Services.Clock;

/// <summary>
/// 正向秒表：以起点时刻与累计量为基准计算已用时间，支持暂停与计圈。
/// 显示层自行按刷新频率取 <see cref="State"/>，引擎本身不产生厘秒级事件。
/// </summary>
public sealed class StopwatchEngine : IStopwatchEngine
{
    private readonly Func<DateTimeOffset> _now;
    private readonly List<TimeSpan> _laps = [];
    private readonly object _gate = new();

    private TimeSpan _accumulated;
    private DateTimeOffset? _startedAt;
    private bool _running;
    private bool _paused;

    public StopwatchEngine(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action<StopwatchState>? Changed;

    public StopwatchState State
    {
        get
        {
            lock (_gate)
            {
                return new StopwatchState(ComputeElapsed(), _running, _paused);
            }
        }
    }

    public IReadOnlyList<TimeSpan> Laps
    {
        get
        {
            lock (_gate)
            {
                return _laps.ToArray();
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _startedAt = _now();
            _running = true;
            _paused = false;
        }

        RaiseChanged();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _accumulated = ComputeElapsed();
            _startedAt = null;
            _running = false;
            _paused = true;
        }

        RaiseChanged();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _accumulated = TimeSpan.Zero;
            _startedAt = null;
            _running = false;
            _paused = false;
            _laps.Clear();
        }

        RaiseChanged();
    }

    public void MarkLap()
    {
        lock (_gate)
        {
            if (!_running && !_paused)
            {
                return;
            }

            _laps.Add(ComputeElapsed());
        }

        RaiseChanged();
    }

    private TimeSpan ComputeElapsed()
    {
        if (_running && _startedAt is { } start)
        {
            return _accumulated + (_now() - start);
        }

        return _accumulated;
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(State);
    }
}
