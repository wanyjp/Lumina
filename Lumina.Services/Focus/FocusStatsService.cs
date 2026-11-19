using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Focus;

/// <summary>
/// 统计视图数据服务（P3-02）：一次性组装本期/上期聚合、逐日序列与今日目标。
/// 与 P3-01 复用同一 <see cref="FocusAggregator"/>，保证月报与近 7/30 天口径一致。
/// </summary>
public sealed class FocusStatsService : IFocusStatsService
{
    private readonly IFocusSessionStore _sessions;
    private readonly IDailyGoalStore _goals;
    private readonly ITodoService? _todos;
    private readonly Func<DateTimeOffset> _now;

    public FocusStatsService(
        IFocusSessionStore sessions,
        IDailyGoalStore goals,
        ITodoService? todos = null,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(goals);

        _sessions = sessions;
        _goals = goals;
        _todos = todos;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<FocusStatsSnapshot> LoadAsync(
        PeriodKind kind,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var now = _now();
        var period = FocusAggregator.Resolve(kind, offset, now);
        var previous = FocusAggregator.Resolve(kind, offset - 1, now);

        var currentSessions = _sessions.EndedBetween(period.StartInclusive, period.EndExclusive);
        var previousSessions = _sessions.EndedBetween(previous.StartInclusive, previous.EndExclusive);

        var currentTodos = await TodoStatsAsync(period, cancellationToken).ConfigureAwait(false);
        var previousTodos = await TodoStatsAsync(previous, cancellationToken).ConfigureAwait(false);

        var daily = BuildDaily(period, currentSessions);
        var today = FocusAggregator.LocalDate(now);
        var todayTarget = Clamp(_goals.GetTargetMinutes(today));

        // 今日单独查询：历史月报里今天通常不在当前区间，但目标卡片仍要有值。
        var todayStart = FocusAggregator.LocalStartOfDay(today);
        var todaySessions = _sessions.EndedBetween(todayStart, todayStart.AddDays(1));
        var todayMinutes = todaySessions
            .Where(session => FocusAggregator.LocalDate(session.EndedAt) == today)
            .Sum(session => Math.Max(0, session.DurationSeconds)) / 60;

        return new FocusStatsSnapshot(
            FocusAggregator.Compute(period, currentSessions, currentTodos),
            FocusAggregator.Compute(previous, previousSessions, previousTodos),
            daily,
            todayMinutes,
            todayTarget);
    }

    private List<FocusDailyPoint> BuildDaily(
        FocusPeriod period,
        IReadOnlyList<FocusSession> sessions)
    {
        var buckets = sessions
            .GroupBy(session => FocusAggregator.LocalDate(session.EndedAt))
            .ToDictionary(
                group => group.Key,
                group => (
                    Seconds: group.Sum(session => Math.Max(0, session.DurationSeconds)),
                    Pomodoro: group.Count(session => session is { Source: FocusSource.Pomodoro, Finished: true })));

        var points = new List<FocusDailyPoint>();
        for (var date = period.Start; date <= period.End; date = date.AddDays(1))
        {
            buckets.TryGetValue(date, out var bucket);
            var minutes = bucket.Seconds / 60;
            var target = Clamp(_goals.GetTargetMinutes(date));
            points.Add(new FocusDailyPoint(
                date,
                minutes,
                bucket.Pomodoro,
                target,
                GoalMet: target > 0 && minutes >= target));
        }

        return points;
    }

    private async Task<TodoCompletionStats?> TodoStatsAsync(
        FocusPeriod period,
        CancellationToken cancellationToken)
    {
        if (_todos is null)
        {
            return null;
        }

        return await _todos
            .CompletionStatsAsync(period.Start, period.End, cancellationToken)
            .ConfigureAwait(false);
    }

    private static int Clamp(int minutes) =>
        Math.Clamp(minutes, 0, FocusAggregator.MaxDailyGoalMinutes);
}
