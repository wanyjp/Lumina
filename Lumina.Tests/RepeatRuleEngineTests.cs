using Lumina.Data.Migrations;
using Lumina.Data.Schedules;
using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-07 周期规则：7 种方式、3 种结束条件、边界与整系列行为。</summary>
public class RepeatRuleEngineTests
{
    [Fact]
    public void Week_range_matches_and_wraps_across_week_boundary()
    {
        var weekdays = Rule(RepeatMode.Week, weekStart: 1, weekEnd: 5);

        Assert.True(RepeatRuleEngine.Matches(weekdays, new DateOnly(2026, 11, 2)));
        Assert.False(RepeatRuleEngine.Matches(weekdays, new DateOnly(2026, 11, 7)));

        var wrapped = Rule(RepeatMode.Week, weekStart: 5, weekEnd: 1);

        Assert.True(RepeatRuleEngine.Matches(wrapped, new DateOnly(2026, 11, 6)));
        Assert.True(RepeatRuleEngine.Matches(wrapped, new DateOnly(2026, 11, 8)));
        Assert.True(RepeatRuleEngine.Matches(wrapped, new DateOnly(2026, 11, 9)));
        Assert.False(RepeatRuleEngine.Matches(wrapped, new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void Month_range_supports_numeric_and_end_bounds()
    {
        var firstWeek = Rule(RepeatMode.Month, monthStart: 1, monthEnd: "7");

        Assert.True(RepeatRuleEngine.Matches(firstWeek, new DateOnly(2026, 11, 1)));
        Assert.False(RepeatRuleEngine.Matches(firstWeek, new DateOnly(2026, 11, 8)));

        var lateMonth = Rule(RepeatMode.Month, monthStart: 20, monthEnd: "end");

        Assert.True(RepeatRuleEngine.Matches(lateMonth, new DateOnly(2026, 11, 20)));
        Assert.True(RepeatRuleEngine.Matches(lateMonth, new DateOnly(2026, 11, 30)));
        Assert.False(RepeatRuleEngine.Matches(lateMonth, new DateOnly(2026, 11, 19)));
    }

    [Fact]
    public void Weekdays_multi_select_uses_sunday_zero()
    {
        var rule = Rule(RepeatMode.Weekdays, weekdays: new[] { 1, 3, 5 });

        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 11, 2)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 11, 3)));
        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 11, 4)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 11, 8)));
    }

    [Fact]
    public void Month_day_skips_missing_dates()
    {
        var thirtyFirst = Rule(RepeatMode.MonthDay, monthDay: "31");

        Assert.True(RepeatRuleEngine.Matches(thirtyFirst, new DateOnly(2026, 1, 31)));
        Assert.False(RepeatRuleEngine.Matches(thirtyFirst, new DateOnly(2026, 4, 30)));
        Assert.False(RepeatRuleEngine.Matches(thirtyFirst, new DateOnly(2026, 2, 28)));

        var monthEnd = Rule(RepeatMode.MonthDay, monthDay: "end");

        Assert.True(RepeatRuleEngine.Matches(monthEnd, new DateOnly(2026, 2, 28)));
        Assert.True(RepeatRuleEngine.Matches(monthEnd, new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void Leap_day_is_generated_only_in_leap_years()
    {
        var rule = Rule(RepeatMode.MonthDay, monthDay: "29");

        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2024, 2, 29)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 2, 28)));
        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void End_conditions_date_count_and_forever()
    {
        var start = new DateOnly(2026, 11, 2);
        var daily = Rule(RepeatMode.Daily);

        var byDate = daily with { EndType = RepeatEndType.Date, EndDate = new DateOnly(2026, 11, 4) };
        var dateOccurrences = RepeatRuleEngine.Occurrences(
            byDate, start, start, new DateOnly(2026, 11, 10));
        Assert.Equal(new[] { start, start.AddDays(1), start.AddDays(2) }, dateOccurrences);

        var byCount = daily with { EndType = RepeatEndType.Count, Count = 3 };
        var countOccurrences = RepeatRuleEngine.Occurrences(
            byCount, start, start, new DateOnly(2026, 11, 10));
        Assert.Equal(3, countOccurrences.Count);

        var forever = daily with { EndType = RepeatEndType.Forever };
        var window = RepeatRuleEngine.Occurrences(
            forever, start, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12));
        Assert.Equal(3, window.Count);
        Assert.Equal(new DateOnly(2026, 11, 10), window[0]);
    }

    [Fact]
    public void Preview_returns_first_five_occurrences()
    {
        var rule = Rule(RepeatMode.Daily);

        var preview = RepeatRuleEngine.Preview(rule, new DateOnly(2026, 11, 2));

        Assert.Equal(5, preview.Count);
        Assert.Equal(new DateOnly(2026, 11, 2), preview[0]);
        Assert.Equal(new DateOnly(2026, 11, 6), preview[^1]);
    }

    [Fact]
    public void Workday_respects_holiday_and_makeup_calendar()
    {
        var rule = Rule(RepeatMode.Workday);

        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 2, 16)));
        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 2, 14)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 3, 7)));
        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 3, 6)));
    }

    [Fact]
    public void Holiday_uses_2026_data_and_weekend_fallback_for_future_years()
    {
        var rule = Rule(RepeatMode.Holiday);

        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 10, 1)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2026, 10, 10)));
        Assert.True(RepeatRuleEngine.Matches(rule, new DateOnly(2027, 1, 2)));
        Assert.False(RepeatRuleEngine.Matches(rule, new DateOnly(2027, 1, 4)));
    }

    [Fact]
    public void Series_scan_is_capped_at_3700_days()
    {
        var rule = Rule(RepeatMode.Daily);

        var occurrences = RepeatRuleEngine.Occurrences(
            rule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            new DateOnly(2036, 12, 31));

        Assert.Equal(RepeatRuleEngine.MaxDaysPerSeries, occurrences.Count);
    }

    [Fact]
    public void Expand_carries_series_id_times_and_duration()
    {
        var master = new ScheduleEvent
        {
            Id = 42,
            Title = "夜班",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(2, 0),
            EndDate = new DateOnly(2026, 11, 3),
            Repeat = Rule(RepeatMode.Daily),
        };

        var instances = RepeatRuleEngine.Expand(
            master,
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 4));

        Assert.Equal(2, instances.Count);
        Assert.All(instances, item => Assert.Equal(42, item.Id));
        Assert.Equal(new DateOnly(2026, 11, 3), instances[0].StartDate);
        Assert.Equal(new DateOnly(2026, 11, 4), instances[0].EndDate);
        Assert.Equal(new TimeOnly(22, 0), instances[0].StartTime);
    }

    [Fact]
    public async Task Calendar_service_expands_recurring_events_in_range()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);
        var calendar = new CalendarService(
            new SettingsStore(new SqliteSettingsBackend(temp.Database)),
            store);

        var series = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "每日站会",
                StartDate = new DateOnly(2026, 11, 2),
                StartTime = new TimeOnly(9, 30),
                Repeat = Rule(RepeatMode.Daily),
            },
            TestContext.Current.CancellationToken);

        var range = await calendar.EventsBetweenAsync(
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 12),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, range.Count);
        Assert.All(range, item => Assert.Equal(series.Id, item.Id));
    }

    [Fact]
    public async Task Series_edit_and_delete_affect_all_occurrences()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);
        var calendar = new CalendarService(
            new SettingsStore(new SqliteSettingsBackend(temp.Database)),
            store);

        var series = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "旧标题",
                StartDate = new DateOnly(2026, 11, 2),
                Repeat = Rule(RepeatMode.Daily),
            },
            TestContext.Current.CancellationToken);

        await service.UpdateAsync(
            series with { Title = "新标题" },
            TestContext.Current.CancellationToken);

        var updated = await calendar.EventsOnAsync(
            new DateOnly(2026, 11, 10),
            TestContext.Current.CancellationToken);
        Assert.Single(updated);
        Assert.Equal("新标题", updated[0].Title);

        await service.DeleteAsync([series.Id], TestContext.Current.CancellationToken);
        Assert.Empty(await calendar.EventsOnAsync(
            new DateOnly(2026, 11, 10),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validation_rejects_invalid_repeat_rules()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new ScheduleService(new ScheduleRepository(temp.Database));
        var start = new DateOnly(2026, 11, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "星期越界",
                StartDate = start,
                Repeat = Rule(RepeatMode.Week, weekStart: 0, weekEnd: 5),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "每月区间倒置",
                StartDate = start,
                Repeat = Rule(RepeatMode.Month, monthStart: 20, monthEnd: "10"),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "星期为空",
                StartDate = start,
                Repeat = Rule(RepeatMode.Weekdays, weekdays: Array.Empty<int>()),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "次数越界",
                StartDate = start,
                Repeat = Rule(RepeatMode.Daily) with
                {
                    EndType = RepeatEndType.Count,
                    Count = 367,
                },
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "周期跨天",
                StartDate = start,
                EndDate = start.AddDays(1),
                Repeat = Rule(RepeatMode.Daily),
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Migration_converts_once_rule_to_single_event()
    {
        using var temp = new TemporaryDataRoot();
        temp.CreateInitializer([MigrationCatalog.All[0], MigrationCatalog.All[1], MigrationCatalog.All[2]])
            .Initialize(TestContext.Current.CancellationToken);

        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schedule_event
                  (title, start_date, repeat_rule, created_at, updated_at)
                VALUES
                  ('旧周期', '2026-11-06', '{"mode":"once","endType":"forever"}',
                   '2026-11-06T00:00:00+08:00', '2026-11-06T00:00:00+08:00');
                """;
            command.ExecuteNonQuery();
        });

        temp.CreateInitializer().Initialize(TestContext.Current.CancellationToken);

        var store = new ScheduleRepository(temp.Database);
        var events = store.QueryRange(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6));

        Assert.Single(events);
        Assert.Null(events[0].Repeat);
    }

    private static RepeatRule Rule(
        RepeatMode mode,
        int? weekStart = null,
        int? weekEnd = null,
        int? monthStart = null,
        string? monthEnd = null,
        string? monthDay = null,
        IReadOnlyList<int>? weekdays = null)
    {
        return new RepeatRule
        {
            Mode = mode,
            EndType = RepeatEndType.Forever,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            MonthStart = monthStart,
            MonthEnd = monthEnd,
            MonthDay = monthDay,
            Weekdays = weekdays,
        };
    }
}
