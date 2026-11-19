using Lumina.Data.Migrations;
using Lumina.Data.Schedules;
using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-06 日程：CRUD、跨天查询、时间语义、校验与旧数据默认值。</summary>
public class ScheduleServiceTests
{
    [Fact]
    public async Task Add_update_and_delete_round_trip()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);

        var created = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "  项目评审  ",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(9, 30),
                Location = " 会议室 A ",
                Priority = Priority.High,
                Reminder = ReminderOffset.Minutes15,
            },
            TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);
        Assert.Equal("项目评审", created.Title);
        Assert.Equal("会议室 A", created.Location);

        var updated = created with { Title = "项目评审（改）", Priority = Priority.Low };
        await service.UpdateAsync(updated, TestContext.Current.CancellationToken);

        var reloaded = store.Find(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("项目评审（改）", reloaded.Title);
        Assert.Equal(Priority.Low, reloaded.Priority);

        await service.DeleteAsync([created.Id], TestContext.Current.CancellationToken);
        Assert.Null(store.Find(created.Id));
    }

    [Fact]
    public async Task Day_query_includes_cross_day_middle_dates()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);
        var calendar = new CalendarService(
            new SettingsStore(new SqliteSettingsBackend(temp.Database)),
            store);

        var crossDay = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "出差",
                StartDate = new DateOnly(2026, 11, 5),
                StartTime = new TimeOnly(22, 0),
                EndDate = new DateOnly(2026, 11, 8),
                EndTime = new TimeOnly(2, 0),
            },
            TestContext.Current.CancellationToken);

        var middle = await calendar.EventsOnAsync(
            new DateOnly(2026, 11, 6),
            TestContext.Current.CancellationToken);

        Assert.Single(middle);
        Assert.Equal(crossDay.Id, middle[0].Id);
        Assert.Empty(await calendar.EventsOnAsync(
            new DateOnly(2026, 11, 9),
            TestContext.Current.CancellationToken));

        var range = await calendar.EventsBetweenAsync(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30),
            TestContext.Current.CancellationToken);

        Assert.Contains(range, item => item.Id == crossDay.Id);
    }

    [Fact]
    public async Task Day_query_orders_all_day_and_ongoing_first()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);
        var calendar = new CalendarService(
            new SettingsStore(new SqliteSettingsBackend(temp.Database)),
            store);

        await service.AddAsync(
            new ScheduleEvent
            {
                Title = "跨天项目",
                StartDate = new DateOnly(2026, 11, 4),
                StartTime = new TimeOnly(9, 0),
                EndDate = new DateOnly(2026, 11, 8),
            },
            TestContext.Current.CancellationToken);

        await service.AddAsync(
            new ScheduleEvent
            {
                Title = "全天事项",
                StartDate = new DateOnly(2026, 11, 6),
            },
            TestContext.Current.CancellationToken);

        await service.AddAsync(
            new ScheduleEvent
            {
                Title = "上午会议",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(8, 0),
            },
            TestContext.Current.CancellationToken);

        await service.AddAsync(
            new ScheduleEvent
            {
                Title = "下午会议",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(14, 0),
            },
            TestContext.Current.CancellationToken);

        var events = await calendar.EventsOnAsync(
            new DateOnly(2026, 11, 6),
            TestContext.Current.CancellationToken);

        Assert.Equal(4, events.Count);
        Assert.Equal("全天事项", events[0].Title);
        Assert.Equal("跨天项目", events[1].Title);
        Assert.Equal("上午会议", events[2].Title);
        Assert.Equal("下午会议", events[3].Title);
    }

    [Fact]
    public async Task All_day_and_open_end_semantics_round_trip()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);

        var allDay = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "全天",
                StartDate = new DateOnly(2026, 11, 6),
            },
            TestContext.Current.CancellationToken);

        var openEnd = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "开放结束",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(9, 0),
            },
            TestContext.Current.CancellationToken);

        Assert.Null(store.Find(allDay.Id)!.StartTime);
        Assert.Null(store.Find(allDay.Id)!.EndTime);
        Assert.Equal(new TimeOnly(9, 0), store.Find(openEnd.Id)!.StartTime);
        Assert.Null(store.Find(openEnd.Id)!.EndTime);
    }

    [Fact]
    public async Task Validation_rejects_empty_title_and_invalid_ranges()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new ScheduleService(new ScheduleRepository(temp.Database));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent { Title = "   ", StartDate = new DateOnly(2026, 11, 6) },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "结束早于开始",
                StartDate = new DateOnly(2026, 11, 6),
                EndDate = new DateOnly(2026, 11, 5),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "同日时间倒置",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(15, 0),
                EndTime = new TimeOnly(9, 0),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            new ScheduleEvent
            {
                Title = "只有结束时间",
                StartDate = new DateOnly(2026, 11, 6),
                EndTime = new TimeOnly(9, 0),
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            new ScheduleEvent { Title = "无 id", StartDate = new DateOnly(2026, 11, 6) },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repeat_rule_is_preserved_through_repository()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);

        var created = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "每周例会",
                StartDate = new DateOnly(2026, 11, 2),
                StartTime = new TimeOnly(10, 0),
                Repeat = new RepeatRule
                {
                    Mode = RepeatMode.Week,
                    EndType = RepeatEndType.Forever,
                    WeekStart = 1,
                    WeekEnd = 5,
                },
            },
            TestContext.Current.CancellationToken);

        var reloaded = store.Find(created.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.Repeat);
        Assert.Equal(RepeatMode.Week, reloaded.Repeat!.Mode);
        Assert.Equal(RepeatEndType.Forever, reloaded.Repeat.EndType);
    }

    [Fact]
    public void Migration_fills_legacy_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.CreateInitializer([MigrationCatalog.All[0], MigrationCatalog.All[1]])
            .Initialize(TestContext.Current.CancellationToken);

        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schedule_event
                  (title, start_date, location, priority, remind, done, created_at, updated_at)
                VALUES
                  ('旧日程', '2026-11-06', NULL, NULL, NULL, NULL,
                   '2026-11-06T00:00:00+08:00', '2026-11-06T00:00:00+08:00');
                """;
            command.ExecuteNonQuery();
        });

        temp.CreateInitializer().Initialize(TestContext.Current.CancellationToken);

        var store = new ScheduleRepository(temp.Database);
        var events = store.QueryRange(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6));

        Assert.Single(events);
        Assert.Equal(string.Empty, events[0].Location);
        Assert.Equal(Priority.Normal, events[0].Priority);
        Assert.Equal(ReminderOffset.None, events[0].Reminder);
        Assert.False(events[0].Done);
    }

    [Fact]
    public async Task Range_query_excludes_events_outside_the_window()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new ScheduleRepository(temp.Database);
        var service = new ScheduleService(store);

        await service.AddAsync(
            new ScheduleEvent
            {
                Title = "十月事件",
                StartDate = new DateOnly(2026, 10, 1),
            },
            TestContext.Current.CancellationToken);

        var result = store.QueryRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Changed_event_fires_for_add_update_and_delete()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new ScheduleService(new ScheduleRepository(temp.Database));
        var changes = 0;
        service.Changed += () => changes++;

        var created = await service.AddAsync(
            new ScheduleEvent
            {
                Title = "同步测试",
                StartDate = new DateOnly(2026, 11, 6),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, changes);

        await service.UpdateAsync(
            created with { Title = "同步测试（改）" },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, changes);

        await service.DeleteAsync([created.Id], TestContext.Current.CancellationToken);

        Assert.Equal(3, changes);
    }
}
