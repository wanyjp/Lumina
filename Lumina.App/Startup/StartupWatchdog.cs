using Lumina.Services.Diagnostics;

namespace Lumina.App.Startup;

/// <summary>
/// 启动首帧看门狗（P4-03）：后台线程每秒检查一次，即使 UI 线程被渲染通道卡住也能记录证据。
/// 首帧超时后由上层切软件渲染；恢复预算内仍无首帧则记录错误并停止轮询。
/// 预算可用环境变量 <c>LUMINA_STARTUP_WATCHDOG_MS</c> 覆盖，供兜底路径实测使用。
/// </summary>
internal sealed class StartupWatchdog : IDisposable
{
    private const int DefaultBudgetMilliseconds = 15_000;
    private const int DefaultPollMilliseconds = 1_000;

    private readonly StartupWatchdogCore _core;
    private readonly Action<StartupWatchdogSignal> _onSignal;
    private readonly TimeSpan _pollInterval;
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    public StartupWatchdog(TimeSpan budget, Action<StartupWatchdogSignal> onSignal)
    {
        ArgumentNullException.ThrowIfNull(onSignal);

        _core = new StartupWatchdogCore(budget, budget);
        _onSignal = onSignal;
        _pollInterval = ResolvePollInterval();
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public static TimeSpan ResolveBudget()
    {
        var raw = Environment.GetEnvironmentVariable("LUMINA_STARTUP_WATCHDOG_MS");
        if (int.TryParse(raw, out var milliseconds) && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return TimeSpan.FromMilliseconds(DefaultBudgetMilliseconds);
    }

    private static TimeSpan ResolvePollInterval()
    {
        var raw = Environment.GetEnvironmentVariable("LUMINA_STARTUP_WATCHDOG_POLL_MS");
        if (int.TryParse(raw, out var milliseconds) && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return TimeSpan.FromMilliseconds(DefaultPollMilliseconds);
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _core.Start();
        _timer.Change(_pollInterval, _pollInterval);
    }

    /// <summary>首帧到达（UI 线程调用）：停止轮询并汇报耗时。</summary>
    public void MarkFirstFrame()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _onSignal(_core.ObserveFrame());
    }

    private void OnTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var signal = _core.ObserveTick();
        if (signal is null)
        {
            return;
        }

        if (signal.State == StartupWatchdogState.Failed)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _onSignal(signal);
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
