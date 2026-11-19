using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Reminders;

/// <summary>
/// 提醒调度器：每 30 秒扫描一次，命中“目标前档位、且距触发不超过 5 分钟”的候选；
/// 休眠/挂起恢复后按上次扫描时间回放窗口，补发错过的提醒。
/// 去重键写入 reminder_dedup，超过 600 条按时间淘汰到 500 条。
/// 系统通道失败时按内存冷却降级到应用内提醒，不修改用户偏好。
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    public const int TickSeconds = 30;

    /// <summary>回放窗口上限：休眠再久也只补最近 7 天，避免一次弹出大量历史提醒。</summary>
    private static readonly TimeSpan MaxReplayWindow = TimeSpan.FromDays(7);

    private static readonly TimeSpan SystemChannelRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>连续失败达到该次数后，本次运行不再尝试系统通知。</summary>
    private const int SystemChannelFailureThreshold = 3;

    private readonly IReadOnlyList<IReminderSource> _sources;
    private readonly IReminderDedupStore _dedup;
    private readonly ISettingsStore _settings;
    private readonly INotifier _notifier;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<ReminderScheduler>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private System.Threading.Timer? _timer;
    private DateTimeOffset? _lastScanAt;
    private DateTimeOffset _systemChannelCooldownUntil = DateTimeOffset.MinValue;
    private int _systemChannelFailures;
    private bool _disposed;

    public ReminderScheduler(
        IEnumerable<IReminderSource> sources,
        IReminderDedupStore dedup,
        ISettingsStore settings,
        INotifier notifier,
        Func<DateTimeOffset>? now = null,
        ILogger<ReminderScheduler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(dedup);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifier);

        _sources = sources.ToArray();
        _dedup = dedup;
        _settings = settings;
        _notifier = notifier;
        _now = now ?? (() => DateTimeOffset.Now);
        _logger = logger;
    }

    public event Action<ReminderNotification>? Triggered;

    public void Start()
    {
        if (_disposed || _timer is not null)
        {
            return;
        }

        _timer = new System.Threading.Timer(
            _ => _ = TickAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(TickSeconds));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>执行一次扫描；返回本次派发的提醒数量。测试用固定时刻调用。</summary>
    public async Task<int> RunOnceAsync(
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || !await _gate.WaitAsync(0, cancellationToken))
        {
            return 0;
        }

        try
        {
            var now = nowOverride ?? _now();

            // 常规窗口为最近 5 分钟；休眠/挂起导致上次扫描更早时，把起点前移补发。
            var from = now - ReminderPlanner.Window;
            if (_lastScanAt is { } lastScan && lastScan < from)
            {
                from = lastScan;
            }

            var earliest = now - MaxReplayWindow;
            if (from < earliest)
            {
                from = earliest;
            }

            var candidates = await CollectCandidatesAsync(from, now, cancellationToken);

            var fired = 0;
            var marked = false;

            foreach (var candidate in candidates.OrderBy(item => item.TriggerAt))
            {
                if (candidate.TriggerAt > now
                    || candidate.TriggerAt <= from
                    || _dedup.Contains(candidate.TriggerKey))
                {
                    continue;
                }

                if (ReminderPlanner.IsMissed(candidate.TriggerAt, now))
                {
                    _logger?.LogInformation(
                        "补发休眠期间错过的提醒：{Key}（触发于 {TriggerAt}）",
                        candidate.TriggerKey,
                        candidate.TriggerAt);
                }

                var (systemDelivered, fallback) = await TrySystemChannelAsync(
                    candidate,
                    cancellationToken);

                var appBar = _settings.Get<bool>(
                    PreferenceKeys.InAppReminder,
                    PreferenceDefaults.InAppReminder) || fallback;

                if (!systemDelivered && !appBar)
                {
                    continue;
                }

                _dedup.Mark(candidate.TriggerKey, now);
                marked = true;
                fired++;

                var notification = new ReminderNotification(
                    candidate.TriggerKey,
                    candidate.Route,
                    candidate.EntityId,
                    candidate.OccurrenceDate,
                    candidate.Offset,
                    candidate.TargetAt,
                    candidate.Title,
                    candidate.Body,
                    systemDelivered,
                    appBar);

                try
                {
                    Triggered?.Invoke(notification);
                }
                catch (Exception)
                {
                    // 单个订阅者异常不得中断其余提醒。
                }
            }

            if (marked && _dedup.Count() > AppLimits.ReminderDedupUpperBound)
            {
                _dedup.TrimTo(AppLimits.ReminderDedupTrimTarget);
            }

            if (_lastScanAt is null || now > _lastScanAt)
            {
                _lastScanAt = now;
            }

            return fired;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ReminderCandidate>> CollectCandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<ReminderCandidate>();

        foreach (var source in _sources)
        {
            try
            {
                results.AddRange(await source.CandidatesAsync(from, to, cancellationToken));
            }
            catch (Exception)
            {
                // 单个来源失败不影响其他提醒来源。
            }
        }

        return results;
    }

    private async Task<(bool SystemDelivered, bool Fallback)> TrySystemChannelAsync(
        ReminderCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (!_settings.Get<bool>(
                PreferenceKeys.SystemNotification,
                PreferenceDefaults.SystemNotification))
        {
            return (false, false);
        }

        // 系统通道瞬时失败（被占用/服务未就绪）不应永久关闭通知：本次运行按冷却重试。
        if (_systemChannelFailures >= SystemChannelFailureThreshold
            || _now() < _systemChannelCooldownUntil)
        {
            return (false, true);
        }

        try
        {
            var delivered = await _notifier.ShowToastAsync(
                candidate.Title,
                candidate.Body,
                ReminderPlanner.RouteId(candidate.Route),
                cancellationToken);

            if (delivered)
            {
                _systemChannelFailures = 0;
                return (true, false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "系统通知发送异常，将在 {Delay} 后重试。", SystemChannelRetryDelay);
        }

        _systemChannelFailures++;
        _systemChannelCooldownUntil = _now().Add(SystemChannelRetryDelay);
        return (false, true);
    }

    private async Task TickAsync()
    {
        try
        {
            await RunOnceAsync();
        }
        catch (Exception)
        {
            // 定时扫描失败不得导致应用退出。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
