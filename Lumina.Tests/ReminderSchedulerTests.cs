using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Reminders;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-12 提醒来源、调度窗口、去重与降级。</summary>
public class ReminderSchedulerTests
{
    private static readonly DateTimeOffset Now = LocalAt(2026, 11, 6, 9, 45);

    [Fact]
    public async Task Scheduler_dispatches_once_within_window_and_persists_dedup()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var trigger = Now.AddMinutes(-1);
        var source = new FakeReminderSource(
            Candidate("sched:1:2026-11-06:m15", trigger, ReminderOffset.Minutes15));

        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        Assert.Equal(1, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.Equal(0, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.Single(notifications);
        Assert.True(notifications[0].AppBarRequested);
        Assert.False(notifications[0].SystemDelivered);

        var dedup = new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database);
        Assert.True(dedup.Contains("sched:1:2026-11-06:m15"));
    }

    [Fact]
    public async Task Scheduler_skips_missed_window()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var source = new FakeReminderSource(
            Candidate("sched:1:2026-11-06:m15", Now.AddMinutes(-6), ReminderOffset.Minutes15));

        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        Assert.Equal(0, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task System_failure_forces_in_app_fallback_without_disabling_preference()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, false);
        settings.Set(PreferenceKeys.SystemNotification, true);

        var source = new FakeReminderSource(
            Candidate("countdown:7:2026-11-07:d1", Now, ReminderOffset.Day1));

        var notifier = new FakeNotifier(false);
        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            notifier);

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        Assert.Equal(1, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.Single(notifications);
        Assert.False(notifications[0].SystemDelivered);
        Assert.True(notifications[0].AppBarRequested);

        // 瞬时失败只做内存降级，不永久关闭用户偏好。
        Assert.True(settings.Get<bool>(PreferenceKeys.SystemNotification));
    }

    [Fact]
    public async Task Scheduler_replays_reminder_missed_during_sleep()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var source = new FakeReminderSource(
            Candidate("sched:9:2026-11-06:ontime", Now.AddMinutes(5), ReminderOffset.OnTime));

        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        // 首次扫描不补发历史提醒。
        Assert.Equal(0, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));

        // 模拟合盖 45 分钟后恢复：窗口从上次扫描时间重放，错过 5 分钟窗口的提醒补发一次。
        Assert.Equal(1, await scheduler.RunOnceAsync(
            Now.AddMinutes(45),
            TestContext.Current.CancellationToken));
        Assert.Single(notifications);
        Assert.Equal("sched:9:2026-11-06:ontime", notifications[0].TriggerKey);
    }

    [Fact]
    public async Task System_channel_retries_after_transient_failure()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, false);
        settings.Set(PreferenceKeys.SystemNotification, true);

        var first = new FakeReminderSource(
            Candidate("sched:1:2026-11-06:ontime", Now, ReminderOffset.OnTime));
        var second = new FakeReminderSource(
            Candidate("sched:2:2026-11-06:ontime", Now.AddMinutes(10), ReminderOffset.OnTime));

        var clock = Now;
        var notifier = new FakeNotifier(false);
        var scheduler = new ReminderScheduler(
            [first, second],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            notifier,
            now: () => clock);

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        Assert.Equal(1, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.False(notifications[0].SystemDelivered);

        // 冷却结束后重试，系统通道恢复可用。
        notifier.Result = true;
        clock = Now.AddMinutes(10);
        Assert.Equal(1, await scheduler.RunOnceAsync(clock, TestContext.Current.CancellationToken));
        Assert.True(notifications[1].SystemDelivered);
    }

    [Fact]
    public async Task Both_channels_disabled_does_not_dispatch_or_mark()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, false);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var source = new FakeReminderSource(
            Candidate("sched:1:2026-11-06:m15", Now, ReminderOffset.Minutes15));

        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(true));

        Assert.Equal(0, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.False(new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database)
            .Contains("sched:1:2026-11-06:m15"));
    }

    [Fact]
    public async Task System_channel_success_does_not_request_app_bar_when_in_app_disabled()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, false);
        settings.Set(PreferenceKeys.SystemNotification, true);

        var source = new FakeReminderSource(
            Candidate("sched:1:2026-11-06:m15", Now, ReminderOffset.Minutes15));

        var scheduler = new ReminderScheduler(
            [source],
            new global::Lumina.Data.Reminders.ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(true));

        var notifications = new List<ReminderNotification>();
        scheduler.Triggered += notifications.Add;

        Assert.Equal(1, await scheduler.RunOnceAsync(Now, TestContext.Current.CancellationToken));
        Assert.True(notifications[0].SystemDelivered);
        Assert.False(notifications[0].AppBarRequested);
    }

    [Fact]
    public async Task Schedule_source_uses_each_events_reminder_and_all_day_anchor()
    {
        var calendar = new FakeCalendarService();
        calendar.Events.Add(new ScheduleEvent
        {
            Id = 1,
            Title = "晨会",
            StartDate = new DateOnly(2026, 11, 6),
            StartTime = new TimeOnly(9, 15),
            Reminder = ReminderOffset.Minutes15,
        });
        calendar.Events.Add(new ScheduleEvent
        {
            Id = 2,
            Title = "全天整理",
            StartDate = new DateOnly(2026, 11, 6),
            Reminder = ReminderOffset.OnTime,
        });

        var source = new ScheduleReminderSource(calendar);
        var candidates = await source.CandidatesAsync(LocalAt(2026, 11, 6, 8, 58), LocalAt(2026, 11, 6, 9, 1), TestContext.Current.CancellationToken);

        Assert.Contains(candidates, item => item.TriggerKey == "sched:1:2026-11-06:m15");
        Assert.Contains(candidates, item => item.TriggerKey == "sched:2:2026-11-06:ontime");
        Assert.Contains(candidates, item => item.TargetAt.Hour == ReminderPlanner.AllDayAnchorHour);
    }

    [Fact]
    public async Task Schedule_source_skips_done_and_none_reminders()
    {
        var calendar = new FakeCalendarService();
        calendar.Events.Add(new ScheduleEvent
        {
            Id = 1,
            Title = "已完成",
            StartDate = new DateOnly(2026, 11, 6),
            StartTime = new TimeOnly(10, 0),
            Reminder = ReminderOffset.Minutes15,
            Done = true,
        });
        calendar.Events.Add(new ScheduleEvent
        {
            Id = 2,
            Title = "不提醒",
            StartDate = new DateOnly(2026, 11, 6),
            StartTime = new TimeOnly(10, 0),
            Reminder = ReminderOffset.None,
        });

        var source = new ScheduleReminderSource(calendar);
        var candidates = await source.CandidatesAsync(LocalAt(2026, 11, 6, 9, 44), LocalAt(2026, 11, 6, 9, 46), TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Recurring_instances_get_distinct_trigger_keys()
    {
        var calendar = new FakeCalendarService();
        foreach (var day in new[] { 5, 6 })
        {
            calendar.Events.Add(new ScheduleEvent
            {
                Id = 5,
                Title = "每日站会",
                StartDate = new DateOnly(2026, 11, day),
                StartTime = new TimeOnly(9, 0),
                Reminder = ReminderOffset.Minutes15,
            });
        }

        var source = new ScheduleReminderSource(calendar);
        var candidates = await source.CandidatesAsync(LocalAt(2026, 11, 5, 8, 44), LocalAt(2026, 11, 6, 8, 46), TestContext.Current.CancellationToken);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, item => item.TriggerKey == "sched:5:2026-11-05:m15");
        Assert.Contains(candidates, item => item.TriggerKey == "sched:5:2026-11-06:m15");
    }

    [Fact]
    public async Task Countdown_source_uses_rolled_next_occurrence()
    {
        var store = new FakeCountdownStore();
        store.Items.Add(new CountdownItem
        {
            Id = 7,
            Name = "每月一号",
            Repeat = CountdownRepeat.Monthly,
            AnchorDay = 1,
            Reminder = ReminderOffset.Day1,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

        var source = new CountdownReminderSource(store);
        var candidates = await source.CandidatesAsync(LocalAt(2026, 11, 30, 8, 55), LocalAt(2026, 11, 30, 9, 1), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal("countdown:7:2026-12-01:d1", candidate.TriggerKey);
        Assert.Equal(new DateOnly(2026, 12, 1), candidate.OccurrenceDate);
        Assert.Equal(9, candidate.TargetAt.Hour);
    }

    private static ReminderCandidate Candidate(
        string key,
        DateTimeOffset trigger,
        ReminderOffset offset) =>
        new(
            key,
            key.StartsWith("countdown", StringComparison.Ordinal)
                ? ReminderRoute.Countdown
                : ReminderRoute.Schedule,
            1,
            DateOnly.FromDateTime(trigger.Date),
            offset,
            trigger,
            trigger.Add(offset.ToLeadTime()),
            "提醒标题",
            "提醒正文");

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));

    private sealed class FakeReminderSource : IReminderSource
    {
        private readonly ReminderCandidate _candidate;

        public FakeReminderSource(ReminderCandidate candidate) => _candidate = candidate;

        public Task<IReadOnlyList<ReminderCandidate>> CandidatesAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderCandidate>>(
                _candidate.TriggerAt >= from && _candidate.TriggerAt <= to
                    ? [_candidate]
                    : []);
    }

    private sealed class FakeNotifier : INotifier
    {
        public FakeNotifier(bool result) => Result = result;

        public bool Result { get; set; }

        public Task<bool> ShowToastAsync(
            string title,
            string body,
            string? route = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class FakeCalendarService : ICalendarService
    {
        public List<ScheduleEvent> Events { get; } = [];

        public CalendarMonthModel BuildMonth(int year, int month) =>
            new(year, month, []);

        public Task<IReadOnlyList<ScheduleEvent>> EventsOnAsync(
            DateOnly date,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduleEvent>>(
                Events.Where(item => item.StartDate == date).ToArray());

        public Task<IReadOnlyList<ScheduleEvent>> EventsBetweenAsync(
            DateOnly start,
            DateOnly end,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduleEvent>>(Events);
    }

    private sealed class FakeCountdownStore : global::Lumina.Domain.Stores.ICountdownStore
    {
        public List<CountdownItem> Items { get; } = [];

        public IReadOnlyList<CountdownItem> QueryAll() => Items;

        public CountdownItem? Find(long id) => Items.Find(item => item.Id == id);

        public long Insert(CountdownItem item, DateTimeOffset now)
        {
            var id = Items.Count + 1L;
            Items.Add(item with { Id = id });
            return id;
        }

        public void Update(CountdownItem item, DateTimeOffset now)
        {
            var index = Items.FindIndex(existing => existing.Id == item.Id);
            if (index >= 0)
            {
                Items[index] = item;
            }
        }

        public int Delete(IEnumerable<long> ids)
        {
            var targets = ids.ToHashSet();
            return Items.RemoveAll(item => targets.Contains(item.Id));
        }
    }
}
