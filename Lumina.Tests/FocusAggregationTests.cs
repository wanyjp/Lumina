using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P3-01 周期解析与专注聚合口径。</summary>
public class FocusAggregationTests
{
    private static readonly DateTimeOffset Now = Local(2026, 11, 10, 13, 30);

    [Fact]
    public void Week7_ends_today_and_covers_seven_days()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Week7, 0, Now);

        Assert.Equal(new DateOnly(2026, 11, 4), period.Start);
        Assert.Equal(new DateOnly(2026, 11, 10), period.End);
        Assert.Equal(
            TimeZoneInfo.Local.GetUtcOffset(period.Start.ToDateTime(TimeOnly.MinValue)),
            period.StartInclusive.Offset);
    }

    [Fact]
    public void Week7_negative_offset_shifts_by_whole_weeks()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Week7, -1, Now);

        Assert.Equal(new DateOnly(2026, 10, 28), period.Start);
        Assert.Equal(new DateOnly(2026, 11, 3), period.End);
    }

    [Fact]
    public void Month30_covers_thirty_days()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Month30, 0, Now);

        Assert.Equal(new DateOnly(2026, 10, 12), period.Start);
        Assert.Equal(new DateOnly(2026, 11, 10), period.End);
    }

    [Fact]
    public void MonthlyArchive_covers_calendar_month_and_handles_previous_month()
    {
        var current = FocusAggregator.Resolve(PeriodKind.MonthlyArchive, 0, Now);
        Assert.Equal(new DateOnly(2026, 11, 1), current.Start);
        Assert.Equal(new DateOnly(2026, 11, 30), current.End);

        var previous = FocusAggregator.Resolve(PeriodKind.MonthlyArchive, -1, Now);
        Assert.Equal(new DateOnly(2026, 10, 1), previous.Start);
        Assert.Equal(new DateOnly(2026, 10, 31), previous.End);
    }

    [Fact]
    public void Sessions_are_attributed_to_end_date_across_midnight()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Week7, 0, Now);

        // 跨天：11-09 23:40 开始，11-10 00:20 结束 → 归入 11-10（区间内）
        var crossing = Session(
            "cross",
            FocusSource.Timer,
            Local(2026, 11, 9, 23, 40),
            Local(2026, 11, 10, 0, 20),
            2400,
            finished: true);

        // 越界：11-11 00:05 结束 → 不在「截至 11-10 的 7 天」区间
        var nextDay = Session(
            "next",
            FocusSource.Timer,
            Local(2026, 11, 10, 23, 0),
            Local(2026, 11, 11, 0, 5),
            3900,
            finished: true);

        var aggregate = FocusAggregator.Compute(period, [crossing, nextDay]);

        Assert.Equal(40, aggregate.FocusMinutes);
        Assert.Equal(0, aggregate.PomodoroCount);
    }

    [Fact]
    public void Unfinished_pomodoro_counts_minutes_but_not_pomodoro_count()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Month30, 0, Now);

        var finished = Session("done", FocusSource.Pomodoro, Now.AddMinutes(-50), Now.AddMinutes(-25), 1500, true);
        var partial = Session("partial", FocusSource.Pomodoro, Now.AddMinutes(-20), Now, 1200, false);
        var timer = Session("timer", FocusSource.Timer, Now.AddMinutes(-80), Now.AddMinutes(-50), 1800, true);

        var aggregate = FocusAggregator.Compute(period, [finished, partial, timer]);

        Assert.Equal(75, aggregate.FocusMinutes);
        Assert.Equal(1, aggregate.PomodoroCount);
    }

    [Fact]
    public void Focus_minutes_floor_partial_minutes()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Week7, 0, Now);
        var session = Session("short", FocusSource.Timer, Now.AddSeconds(-119), Now, 119, true);

        var aggregate = FocusAggregator.Compute(period, [session]);

        Assert.Equal(1, aggregate.FocusMinutes);
    }

    [Fact]
    public void Todo_stats_are_passed_through()
    {
        var period = FocusAggregator.Resolve(PeriodKind.Month30, 0, Now);
        var todos = new TodoCompletionStats(period.Start, period.End, Completed: 3, Total: 5);

        var aggregate = FocusAggregator.Compute(period, [], todos);

        Assert.Equal(3, aggregate.CompletedTodos);
        Assert.Equal(5, aggregate.TotalTodos);
        Assert.Equal(0.6, (double)aggregate.CompletedTodos / aggregate.TotalTodos, 3);
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static FocusSession Session(
        string key,
        FocusSource source,
        DateTimeOffset started,
        DateTimeOffset ended,
        int seconds,
        bool finished) => new()
        {
            SessionKey = key,
            Source = source,
            StartedAt = started,
            EndedAt = ended,
            DurationSeconds = seconds,
            Finished = finished,
        };
}
