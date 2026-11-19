using Lumina.Data.Countdowns;
using Lumina.Data.Focus;
using Lumina.Data.Notes;
using Lumina.Data.Reminders;
using Lumina.Data.Schedules;
using Lumina.Data.Settings;
using Lumina.Data.Todos;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Countdowns;
using Lumina.Services.Focus;
using Lumina.Services.Notes;
using Lumina.Services.Reminders;
using Lumina.Services.Settings;
using Lumina.Services.Todos;

namespace Lumina.Tests;

/// <summary>
/// P3-10 M3 跨模块一致性：待办完成数与专注统计同口径、便签转待办回链幂等、
/// 日程与倒计时提醒共用去重、日历“今日安排”两条查询路径一致。
/// </summary>
public class M3IntegrationTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);
    private static readonly DateTimeOffset Now = LocalAt(Today, new TimeOnly(9, 0, 30));

    [Fact]
    public async Task Todo_completion_and_focus_stats_report_the_same_period_numbers()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var todos = new TodoService(
            new TodoRepository(temp.Database),
            new NoteRepository(temp.Database),
            () => Now);

        var first = await todos.AddAsync(Todo("写 M3 报告"), TestContext.Current.CancellationToken);
        var second = await todos.AddAsync(Todo("复核安全项"), TestContext.Current.CancellationToken);
        await todos.AddAsync(Todo("整理截图"), TestContext.Current.CancellationToken);

        await todos.ToggleDoneAsync(first.Id, TestContext.Current.CancellationToken);
        await todos.ToggleDoneAsync(second.Id, TestContext.Current.CancellationToken);

        var stats = new FocusStatsService(
            new FocusSessionRepository(temp.Database),
            new DailyGoalRepository(temp.Database),
            todos,
            () => Now);
        var snapshot = await stats.LoadAsync(
            PeriodKind.Week7,
            0,
            TestContext.Current.CancellationToken);

        var todoStats = await todos.CompletionStatsAsync(
            snapshot.Current.Start,
            snapshot.Current.End,
            TestContext.Current.CancellationToken);

        Assert.Equal(todoStats.Completed, snapshot.Current.CompletedTodos);
        Assert.Equal(todoStats.Total, snapshot.Current.TotalTodos);
        Assert.Equal(2, snapshot.Current.CompletedTodos);
        Assert.Equal(3, snapshot.Current.TotalTodos);
    }

    [Fact]
    public async Task Note_to_todo_is_idempotent_and_keeps_backlink()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var notes = new NoteService(new NoteRepository(temp.Database), () => Now);
        var todos = new TodoService(
            new TodoRepository(temp.Database),
            new NoteRepository(temp.Database),
            () => Now);

        var note = await notes.SaveAsync(
            new NoteItem
            {
                Title = "采购清单",
                Content = "☐ 牛奶\n☐ 咖啡豆",
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        var created = await todos.ConvertFromNoteAsync(
            note.Id,
            TestContext.Current.CancellationToken);
        var again = await todos.ConvertFromNoteAsync(
            note.Id,
            TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal(note.Id, created.NoteId);
        var listed = await todos.ListAsync(null, TestContext.Current.CancellationToken);
        Assert.Single(listed);
    }

    [Fact]
    public async Task Schedule_and_countdown_reminders_fire_together_and_deduplicate()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        settings.Set(PreferenceKeys.InAppReminder, true, module: "reminders");
        settings.Set(PreferenceKeys.SystemNotification, false, module: "reminders");

        var schedules = new ScheduleService(new ScheduleRepository(temp.Database), () => Now);
        var calendar = new CalendarService(settings, new ScheduleRepository(temp.Database));
        await schedules.AddAsync(
            new ScheduleEvent
            {
                Title = "M3 验收会",
                StartDate = Today,
                StartTime = new TimeOnly(9, 0),
                Reminder = ReminderOffset.OnTime,
            },
            TestContext.Current.CancellationToken);

        var countdowns = new CountdownService(new CountdownRepository(temp.Database), () => Now);
        await countdowns.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "项目纪念日",
                Repeat = CountdownRepeat.None,
                AnchorDate = Today.AddDays(1),
                Reminder = ReminderOffset.Day1,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        var scheduler = new ReminderScheduler(
            [new ScheduleReminderSource(calendar), new CountdownReminderSource(new CountdownRepository(temp.Database))],
            new ReminderDedupRepository(temp.Database),
            settings,
            new SilentNotifier(),
            () => Now);

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        var firstPass = await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken);
        var secondPass = await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(2, firstPass);
        Assert.Equal(0, secondPass);
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, item => item.Route == ReminderRoute.Schedule);
        Assert.Contains(notifications, item => item.Route == ReminderRoute.Countdown);
    }

    [Fact]
    public async Task Calendar_today_and_range_queries_agree()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var schedules = new ScheduleService(new ScheduleRepository(temp.Database), () => Now);
        var calendar = new CalendarService(settings, new ScheduleRepository(temp.Database));

        await schedules.AddAsync(
            new ScheduleEvent
            {
                Title = "今日评审",
                StartDate = Today,
                StartTime = new TimeOnly(10, 0),
            },
            TestContext.Current.CancellationToken);

        var today = await calendar.EventsOnAsync(Today, TestContext.Current.CancellationToken);
        var range = await calendar.EventsBetweenAsync(Today, Today, TestContext.Current.CancellationToken);

        Assert.Equal(
            range.Select(item => item.Id).OrderBy(id => id),
            today.Select(item => item.Id).OrderBy(id => id));
        Assert.Single(today);
    }

    private static TodoItem Todo(string text) => new()
    {
        Text = text,
        DueDate = Today,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static DateTimeOffset LocalAt(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class SilentNotifier : INotifier
    {
        public Task<bool> ShowToastAsync(
            string title,
            string body,
            string? route = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
