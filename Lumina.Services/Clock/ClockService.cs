using System.Threading;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clock;

/// <summary>
/// P1-01 正式时钟服务：以 UTC 为基准提供本地时间快照，秒级 Tick 对齐整秒触发。
/// 农历由 <see cref="ClockTextFormatter"/> 从系统日历计算；格式化留给显示层。
/// </summary>
public sealed class ClockService : IClockService, IDisposable
{
    private readonly Timer _timer;
    private bool _disposed;

    public ClockService()
    {
        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        ScheduleNextTick();
    }

    public event Action? TickSecond;

    public ClockSnapshot Now(bool use12Hour, bool shichenMode)
    {
        var local = DateTimeOffset.Now;
        return new ClockSnapshot(
            local.ToUniversalTime(),
            local,
            DateOnly.FromDateTime(local.DateTime),
            ClockTextFormatter.LunarText(DateOnly.FromDateTime(local.DateTime)),
            SelectedCityLabel: null);
    }

    public IReadOnlyList<WorldClockItem> WorldClocks() => Array.Empty<WorldClockItem>();

    private void OnTimerTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            TickSecond?.Invoke();
        }
        catch (Exception)
        {
            // 单个订阅者异常不得中断秒级时钟。
        }

        if (!_disposed)
        {
            ScheduleNextTick();
        }
    }

    private void ScheduleNextTick()
    {
        var now = DateTimeOffset.Now;
        var delay = TimeSpan.FromMilliseconds(Math.Max(1, 1000 - now.Millisecond));
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
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
