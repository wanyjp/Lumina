using Lumina.Services.Reminders;

namespace Lumina.Services.Diagnostics;

/// <summary>一次唤醒/解锁恢复的结果。</summary>
public sealed record ResumeOutcome(
    string Reason,
    TimeSpan DormantFor,
    bool RemindersRescanned,
    int RemindersFired);

/// <summary>
/// 睡眠唤醒 / 会话解锁后的恢复协调（P4-03）：
/// 通过后台心跳判断真实休眠时长，超过阈值才补扫提醒，避免唤醒与解锁两个事件重复扫描；
/// 判定为“短暂停留”时只记录不扫描。提醒窗口仍是 5 分钟，休眠期间错过的更早提醒不会被补发。
/// </summary>
public sealed class SystemResumeCoordinator
{
    private readonly ReminderScheduler _scheduler;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _minimumDormancy;
    private readonly object _gate = new();

    private DateTimeOffset _lastHeartbeat;

    public SystemResumeCoordinator(
        ReminderScheduler scheduler,
        Func<DateTimeOffset>? now = null,
        TimeSpan? minimumDormancy = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        _scheduler = scheduler;
        _now = now ?? (() => DateTimeOffset.Now);
        _minimumDormancy = minimumDormancy ?? TimeSpan.FromSeconds(60);
        if (_minimumDormancy < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDormancy), "休眠阈值不能为负。");
        }

        _lastHeartbeat = _now();
    }

    /// <summary>由后台心跳定时器调用；休眠期间不会执行，因此间隔即休眠时长。</summary>
    public void MarkHeartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeat = _now();
        }
    }

    public async Task<ResumeOutcome> HandleResumeAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = _now();

        TimeSpan dormantFor;
        lock (_gate)
        {
            dormantFor = now - _lastHeartbeat;
            _lastHeartbeat = now;
        }

        if (dormantFor < _minimumDormancy)
        {
            return new ResumeOutcome(reason, dormantFor, RemindersRescanned: false, RemindersFired: 0);
        }

        var fired = await _scheduler
            .RunOnceAsync(now, cancellationToken)
            .ConfigureAwait(false);

        return new ResumeOutcome(reason, dormantFor, RemindersRescanned: true, fired);
    }
}
