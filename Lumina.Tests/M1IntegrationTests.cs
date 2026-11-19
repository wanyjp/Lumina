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
using Lumina.Services.Reminders;
using Lumina.Services.Settings;
using Lumina.Services.Todos;

namespace Lumina.Tests;

/// <summary>P1-14 M1 集成回归：日程→提醒→去重、待办完成率、设置重载持久化。</summary>
public class M1IntegrationTests
{
    [Fact]
    public async Task Core_flow_schedule_reminder_todo_and_preferences_persist()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        settings.Set(PreferenceKeys.InAppReminder, true, module: "reminders");
        settings.Set(PreferenceKeys.SystemNotification, false, module: "reminders");

        var schedules = new ScheduleService(new ScheduleRepository(temp.Database));
        var calendar = new CalendarService(settings, new ScheduleRepository(temp.Database));
        var todos = new TodoService(new TodoRepository(temp.Database), new NoteRepository(temp.Database));
        var now = LocalAt(2026, 11, 6, 9, 45);

        var created = await schedules.AddAsync(
            new ScheduleEvent
            {
                Title = "M1 集成评审",
                StartDate = new DateOnly(2026, 11, 6),
                StartTime = new TimeOnly(10, 0),
                Reminder = ReminderOffset.Minutes15,
                Priority = Priority.High,
                Location = "三楼会议室",
            },
            TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);

        var scheduler = new ReminderScheduler(
            [new ScheduleReminderSource(calendar)],
            new ReminderDedupRepository(temp.Database),
            settings,
            new SilentNotifier(),
            () => now);

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        Assert.Equal(1, await scheduler.RunOnceAsync(now, TestContext.Current.CancellationToken));
        Assert.Equal(0, await scheduler.RunOnceAsync(now, TestContext.Current.CancellationToken));
        Assert.Single(notifications);
        Assert.Equal("日程提醒 · M1 集成评审", notifications[0].Title);
        Assert.Contains("三楼会议室", notifications[0].Body);

        var todo = await todos.AddAsync(
            new TodoItem
            {
                Text = "完成 M1 集成回归",
                DueDate = new DateOnly(2026, 11, 6),
                Priority = Priority.High,
                CreatedAt = now,
                UpdatedAt = now,
            },
            TestContext.Current.CancellationToken);

        await todos.ToggleDoneAsync(todo.Id, TestContext.Current.CancellationToken);

        var stats = await todos.CompletionStatsAsync(
            DateOnly.MinValue,
            DateOnly.MaxValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, stats.Total);
        Assert.Equal(1, stats.Completed);
        Assert.Equal(1.0, stats.CompletionRate);

        settings.Set(PreferenceKeys.CheckStyle, "ring", module: "todo");
        settings.Set(PreferenceKeys.CalendarLayout, "vertical", module: "calendar");

        var reloaded = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        Assert.Equal("ring", reloaded.Get<string>(PreferenceKeys.CheckStyle));
        Assert.Equal("vertical", reloaded.Get<string>(PreferenceKeys.CalendarLayout));
    }

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
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
