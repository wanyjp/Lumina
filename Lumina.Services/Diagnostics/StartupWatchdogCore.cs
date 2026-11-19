namespace Lumina.Services.Diagnostics;

/// <summary>启动看门狗状态：等待首帧 → 超时降级 → 恢复或失败。</summary>
public enum StartupWatchdogState
{
    Idle,
    Waiting,
    Rendered,
    Degraded,
    Failed,
}

/// <summary>看门狗产生的一次状态迁移，用于日志与兜底动作。</summary>
public sealed record StartupWatchdogSignal(
    StartupWatchdogState State,
    TimeSpan Elapsed,
    int Attempt,
    string Detail)
{
    /// <summary>是否在降级之后才拿到首帧（说明软件渲染兜底生效）。</summary>
    public bool RecoveredAfterFallback => State == StartupWatchdogState.Rendered && Attempt > 0;
}

/// <summary>
/// 启动首帧看门狗状态机（P4-03）。只做时间判定，不依赖 WPF，便于用假时钟覆盖：
/// 首帧预算内正常 → <see cref="StartupWatchdogState.Rendered"/>；
/// 超时 → <see cref="StartupWatchdogState.Degraded"/>（上层切软件渲染）；
/// 恢复预算内仍无首帧 → <see cref="StartupWatchdogState.Failed"/>。
/// 降级后拿到首帧会返回 Attempt=1 的 Rendered 信号，用于记录“兜底成功”。
/// </summary>
public sealed class StartupWatchdogCore
{
    private readonly TimeSpan _firstFrameBudget;
    private readonly TimeSpan _recoveryBudget;
    private readonly Func<DateTimeOffset> _now;

    private readonly object _gate = new();

    private DateTimeOffset _startedAt;
    private DateTimeOffset _degradedAt;
    private bool _started;

    public StartupWatchdogCore(
        TimeSpan firstFrameBudget,
        TimeSpan? recoveryBudget = null,
        Func<DateTimeOffset>? now = null)
    {
        if (firstFrameBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFrameBudget), "首帧预算必须大于零。");
        }

        _firstFrameBudget = firstFrameBudget;
        _recoveryBudget = recoveryBudget ?? firstFrameBudget;
        if (_recoveryBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryBudget), "恢复预算必须大于零。");
        }

        _now = now ?? (() => DateTimeOffset.Now);
    }

    public StartupWatchdogState State { get; private set; } = StartupWatchdogState.Idle;

    public bool UsedFallback { get; private set; }

    public StartupWatchdogSignal Start()
    {
        lock (_gate)
        {
            _startedAt = _now();
            _started = true;
            State = StartupWatchdogState.Waiting;
            return new StartupWatchdogSignal(State, TimeSpan.Zero, 0, "watchdog started");
        }
    }

    /// <summary>首帧到达（ContentRendered / 首次 Rendering）。</summary>
    public StartupWatchdogSignal ObserveFrame()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return new StartupWatchdogSignal(StartupWatchdogState.Idle, TimeSpan.Zero, 0, "watchdog not started");
            }

            var elapsed = _now() - _startedAt;
            var previous = State;
            if (previous is StartupWatchdogState.Waiting
                or StartupWatchdogState.Degraded
                or StartupWatchdogState.Failed)
            {
                State = StartupWatchdogState.Rendered;
                var attempt = previous switch
                {
                    StartupWatchdogState.Degraded => 1,
                    StartupWatchdogState.Failed => 2,
                    _ => 0,
                };

                return new StartupWatchdogSignal(
                    State,
                    elapsed,
                    attempt,
                    attempt == 0 ? "first frame" : "first frame after software fallback");
            }

            return new StartupWatchdogSignal(State, elapsed, 0, "late frame ignored");
        }
    }

    /// <summary>轮询检查；未到阈值返回 null，避免上层频繁处理。</summary>
    public StartupWatchdogSignal? ObserveTick()
    {
        lock (_gate)
        {
            if (!_started || State is StartupWatchdogState.Rendered or StartupWatchdogState.Failed)
            {
                return null;
            }

            var now = _now();
            var elapsed = now - _startedAt;

            if (State == StartupWatchdogState.Waiting)
            {
                if (elapsed < _firstFrameBudget)
                {
                    return null;
                }

                State = StartupWatchdogState.Degraded;
                UsedFallback = true;
                _degradedAt = now;
                return new StartupWatchdogSignal(State, elapsed, 1, "first frame timeout");
            }

            if (now - _degradedAt >= _recoveryBudget)
            {
                State = StartupWatchdogState.Failed;
                return new StartupWatchdogSignal(State, elapsed, 2, "recovery timeout");
            }

            return null;
        }
    }
}
