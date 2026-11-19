using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;

namespace Lumina.Services.Focus;

/// <summary>聚合区间的本地自然日范围与对应的 UTC 查询边界。</summary>
public readonly record struct FocusPeriod(
    PeriodKind Kind,
    DateOnly Start,
    DateOnly End,
    DateTimeOffset StartInclusive,
    DateTimeOffset EndExclusive);

/// <summary>
/// 专注时长与周期聚合（P3-01，纯逻辑）。
/// <para>
/// 口径：
/// 1. 会话按**结束时间所在本地自然日**归属，跨天会话不会拆成两天；
/// 2. 专注时长累加全部会话（含中途停止的番茄），向下取整到分钟；
/// 3. 番茄数只统计「自然完成」的番茄会话，重开与中途停止不计；
/// 4. 待办完成率由调用方传入同区间统计，缺省为 0。
/// </para>
/// </summary>
public static class FocusAggregator
{
    /// <summary>每日目标上限（分钟）。</summary>
    public const int MaxDailyGoalMinutes = 1440;

    /// <summary>
    /// 解析周期范围。<c>offset = 0</c> 为当前周期，负数向过去平移：
    /// `Week7` 为截至今天的 7 天，`Month30` 为截至今天的 30 天，`MonthlyArchive` 为整月。
    /// </summary>
    public static FocusPeriod Resolve(PeriodKind kind, int offset, DateTimeOffset now)
    {
        var today = LocalDate(now);

        DateOnly start;
        DateOnly end;

        switch (kind)
        {
            case PeriodKind.Week7:
                end = today.AddDays(7 * offset);
                start = end.AddDays(-6);
                break;
            case PeriodKind.Month30:
                end = today.AddDays(30 * offset);
                start = end.AddDays(-29);
                break;
            case PeriodKind.MonthlyArchive:
                var month = new DateOnly(today.Year, today.Month, 1).AddMonths(offset);
                start = month;
                end = month.AddMonths(1).AddDays(-1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的统计周期。");
        }

        var startLocal = LocalStartOfDay(start);
        var endExclusive = LocalStartOfDay(end.AddDays(1));

        return new FocusPeriod(kind, start, end, startLocal, endExclusive);
    }

    /// <summary>把时刻换算成本地时区后取自然日，避免用 UTC 偏移误判日期。</summary>
    public static DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local).DateTime);

    /// <summary>按当天的真实本地偏移（含夏令时）构造自然日起点。</summary>
    public static DateTimeOffset LocalStartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);

        try
        {
            return new DateTimeOffset(local, offset);
        }
        catch (ArgumentException)
        {
            // 夏令时跳变的不存在时刻：顺延一小时取有效偏移。
            var shifted = local.AddHours(1);
            return new DateTimeOffset(shifted, TimeZoneInfo.Local.GetUtcOffset(shifted));
        }
    }

    /// <summary>按区间聚合并结合待办完成统计。</summary>
    public static PeriodAggregate Compute(
        FocusPeriod period,
        IEnumerable<FocusSession> sessions,
        TodoCompletionStats? todos = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        long seconds = 0;
        var pomodoro = 0;

        foreach (var session in sessions)
        {
            var endedOn = LocalDate(session.EndedAt);
            if (endedOn < period.Start || endedOn > period.End)
            {
                continue;
            }

            if (session.DurationSeconds > 0)
            {
                seconds += session.DurationSeconds;
            }

            if (session is { Source: FocusSource.Pomodoro, Finished: true })
            {
                pomodoro++;
            }
        }

        return new PeriodAggregate(
            period.Kind,
            period.Start,
            period.End,
            FocusMinutes: (int)(seconds / 60),
            PomodoroCount: pomodoro,
            CompletedTodos: todos?.Completed ?? 0,
            TotalTodos: todos?.Total ?? 0);
    }
}
