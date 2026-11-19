using Lumina.Data.Focus;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P3-02 统计快照：逐日序列、目标达成、本期/上期与今日目标。</summary>
public class FocusStatsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 13, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Snapshot_builds_daily_series_goals_and_previous_period()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var sessions = new FocusSessionRepository(temp.Database);
        var goals = new DailyGoalRepository(temp.Database);
        var service = new FocusStatsService(sessions, goals, todos: null, now: () => Now);

        goals.SetTargetMinutes(new DateOnly(2026, 11, 10), 60);
        goals.SetTargetMinutes(new DateOnly(2026, 11, 9), 30);
        goals.SetTargetMinutes(new DateOnly(2026, 11, 5), 45);

        sessions.Add(Session("today", FocusSource.Pomodoro, Now.AddMinutes(-25), Now, 1500, true));
        sessions.Add(Session(
            "yesterday",
            FocusSource.Timer,
            Now.AddDays(-1).AddMinutes(-30),
            Now.AddDays(-1),
            1800,
            true));
        sessions.Add(Session(
            "last-week",
            FocusSource.Timer,
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(8)).AddMinutes(-10),
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(8)),
            600,
            true));

        var snapshot = await service.LoadAsync(
            PeriodKind.Week7,
            0,
            TestContext.Current.CancellationToken);

        Assert.Equal(7, snapshot.Daily.Count);
        Assert.Equal(55, snapshot.Current.FocusMinutes);
        Assert.Equal(1, snapshot.Current.PomodoroCount);
        Assert.False(snapshot.IsEmpty);

        var today = snapshot.Daily.Single(point => point.Date == new DateOnly(2026, 11, 10));
        Assert.Equal(25, today.FocusMinutes);
        Assert.Equal(60, today.TargetMinutes);
        Assert.False(today.GoalMet);

        var yesterday = snapshot.Daily.Single(point => point.Date == new DateOnly(2026, 11, 9));
        Assert.Equal(30, yesterday.FocusMinutes);
        Assert.True(yesterday.GoalMet);

        var noRecord = snapshot.Daily.Single(point => point.Date == new DateOnly(2026, 11, 5));
        Assert.Equal(0, noRecord.FocusMinutes);
        Assert.Equal(45, noRecord.TargetMinutes);
        Assert.False(noRecord.GoalMet);

        Assert.Equal(10, snapshot.Previous.FocusMinutes);
        Assert.Equal(25, snapshot.TodayFocusMinutes);
        Assert.Equal(60, snapshot.TodayTargetMinutes);
    }

    [Fact]
    public async Task Archive_uses_calendar_month_and_still_reports_today_goal()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var sessions = new FocusSessionRepository(temp.Database);
        var goals = new DailyGoalRepository(temp.Database);
        var service = new FocusStatsService(sessions, goals, todos: null, now: () => Now);

        goals.SetTargetMinutes(new DateOnly(2026, 11, 10), 45);
        sessions.Add(Session("today", FocusSource.Pomodoro, Now.AddMinutes(-25), Now, 1500, true));

        var november = await service.LoadAsync(
            PeriodKind.MonthlyArchive,
            0,
            TestContext.Current.CancellationToken);

        Assert.Equal(30, november.Daily.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), november.Current.Start);
        Assert.Equal(new DateOnly(2026, 11, 30), november.Current.End);
        Assert.Equal(25, november.TodayFocusMinutes);
        Assert.Equal(45, november.TodayTargetMinutes);

        var october = await service.LoadAsync(
            PeriodKind.MonthlyArchive,
            -1,
            TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 10, 1), october.Current.Start);
        Assert.Equal(31, october.Daily.Count);
        Assert.True(october.IsEmpty);
    }

    [Fact]
    public async Task Empty_period_is_reported_as_empty_not_zero_cards()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var service = new FocusStatsService(
            new FocusSessionRepository(temp.Database),
            new DailyGoalRepository(temp.Database),
            todos: null,
            now: () => Now);

        var snapshot = await service.LoadAsync(
            PeriodKind.Week7,
            -5,
            TestContext.Current.CancellationToken);

        Assert.True(snapshot.IsEmpty);
        Assert.All(snapshot.Daily, point => Assert.Equal(0, point.FocusMinutes));
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
