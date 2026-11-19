using Lumina.Data.Clipboard;
using Lumina.Data.Reminders;
using Lumina.Data.Schedules;
using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Reminders;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>
/// P4-02 长时逻辑稳定性：用假时钟把 24 小时压缩成秒级回归，
/// 覆盖提醒调度（2880 次 30s tick）与剪贴板 24 小时高频churn（1200 条）。
/// 内存驻留由 soak 脚本单独采样，这里只保证逻辑不退化、去重表不膨胀。
/// </summary>
public class LongRunStabilityTests
{
    // 用本地时区构造起点，避免 CI（UTC）与开发机（+08）跨时区导致提醒窗口错位。
    private static readonly DateTimeOffset Start = ReminderPlanner.LocalAt(
        new DateOnly(2026, 11, 15),
        TimeOnly.MinValue);

    [Fact]
    public async Task Reminder_scheduler_survives_24h_of_ticks_with_dedup()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        settings.Set(PreferenceKeys.InAppReminder, true, module: "reminders");
        settings.Set(PreferenceKeys.SystemNotification, false, module: "reminders");

        var schedules = new ScheduleService(new ScheduleRepository(temp.Database), () => Start);
        var calendar = new CalendarService(settings, new ScheduleRepository(temp.Database));
        foreach (var hour in new[] { 6, 12, 18, 23 })
        {
            await schedules.AddAsync(
                new ScheduleEvent
                {
                    Title = $"24h 回归 {hour}:00",
                    StartDate = DateOnly.FromDateTime(Start.Date),
                    StartTime = new TimeOnly(hour, 0),
                    Reminder = ReminderOffset.OnTime,
                },
                TestContext.Current.CancellationToken);
        }

        var scheduler = new ReminderScheduler(
            [new ScheduleReminderSource(calendar)],
            new ReminderDedupRepository(temp.Database),
            settings,
            new SilentNotifier(),
            () => Start);

        var fired = 0;
        scheduler.Triggered += _ => fired++;

        var ticks = (int)TimeSpan.FromHours(24).TotalSeconds / ReminderScheduler.TickSeconds;
        for (var index = 0; index < ticks; index++)
        {
            var now = Start.AddSeconds((index + 1) * ReminderScheduler.TickSeconds);
            await scheduler.RunOnceAsync(now, TestContext.Current.CancellationToken);
        }

        Assert.Equal(4, fired);
    }

    [Fact]
    public void Clipboard_survives_24h_of_churn_and_keeps_pinned_item()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);
        var now = Start;

        var pinned = NewItem("置顶项", now);
        var saved = repository.Save(pinned, now, TimeSpan.FromSeconds(30), 200);
        var pinnedId = saved.Item.Id;
        repository.SetPinned(pinnedId, true, now);

        for (var index = 0; index < 1200; index++)
        {
            now = now.AddMinutes(1); // 24 小时 × 50 条/小时
            repository.Save(NewItem($"churn-{index}", now), now, TimeSpan.FromSeconds(30), 200);
        }

        var recent = repository.QueryRecent(500);
        Assert.True(recent.Count(item => !item.Pinned) <= 200);
        Assert.Contains(recent, item => item.Id == pinnedId && item.Pinned);
    }

    private static ClipboardItem NewItem(string text, DateTimeOffset now) => new()
    {
        Kind = ClipboardKind.Text,
        TextContent = text,
        NormalizedText = text,
        Mime = "text/plain; charset=utf-8",
        ContentHash = ClipClassifier.ComputeTextHash(ClipboardKind.Text, text),
        SourceWindow = "long-run-test",
        CreatedAt = now,
        UpdatedAt = now,
    };

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
