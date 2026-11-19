using Lumina.Data.Focus;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P3-01 专注会话入库、幂等、聚合与每日目标。</summary>
public class FocusSessionStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 13, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Add_is_idempotent_by_session_key()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new FocusSessionRepository(temp.Database);

        var session = Session("key-1", FocusSource.Pomodoro, Now.AddMinutes(-25), Now, 1500, true);

        Assert.Equal(1, store.Add(session));
        Assert.Equal(0, store.Add(session with { DurationSeconds = 999 }));
        Assert.Equal(1, store.Count());

        var saved = Assert.Single(store.Recent(10));
        Assert.Equal(1500, saved.DurationSeconds);
    }

    [Fact]
    public void EndedBetween_normalizes_offsets_and_orders_by_end_time()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new FocusSessionRepository(temp.Database);

        var early = Session(
            "early",
            FocusSource.Timer,
            new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 11, 9, 8, 30, 0, TimeSpan.FromHours(8)),
            1800,
            true);

        // UTC 记录等价于 +08 的 10:00–10:25，用于验证跨偏移比较
        var utc = Session(
            "utc",
            FocusSource.Pomodoro,
            new DateTimeOffset(2026, 11, 10, 1, 35, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero),
            1500,
            true);

        store.Add(early);
        store.Add(utc);

        var dayStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(8));
        var dayEnd = dayStart.AddDays(1);

        var sessions = store.EndedBetween(dayStart, dayEnd);

        Assert.Single(sessions);
        Assert.Equal("utc", sessions[0].SessionKey);
        Assert.Equal(FocusSource.Pomodoro, sessions[0].Source);
    }

    [Fact]
    public void Round_trip_preserves_source_finished_and_note()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new FocusSessionRepository(temp.Database);

        var session = Session("round", FocusSource.Timer, Now.AddMinutes(-10), Now, 600, false) with
        {
            Note = "写验收报告",
        };

        store.Add(session);

        var saved = Assert.Single(store.Recent(1));
        Assert.Equal(FocusSource.Timer, saved.Source);
        Assert.False(saved.Finished);
        Assert.Equal("写验收报告", saved.Note);
        Assert.Equal(Now, saved.EndedAt);
    }

    [Fact]
    public void Daily_goal_round_trips_and_upserts()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var goals = new DailyGoalRepository(temp.Database);
        var date = new DateOnly(2026, 11, 10);

        Assert.Equal(0, goals.GetTargetMinutes(date));

        goals.SetTargetMinutes(date, 120);
        Assert.Equal(120, goals.GetTargetMinutes(date));

        goals.SetTargetMinutes(date, 90);
        Assert.Equal(90, goals.GetTargetMinutes(date));
    }

    [Fact]
    public async Task Daily_goal_service_clamps_to_documented_range()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new DailyGoalService(new DailyGoalRepository(temp.Database));
        var date = new DateOnly(2026, 11, 10);

        await service.SetTargetMinutesAsync(date, -30, TestContext.Current.CancellationToken);
        Assert.Equal(0, await service.GetTargetMinutesAsync(date, TestContext.Current.CancellationToken));

        await service.SetTargetMinutesAsync(date, 5000, TestContext.Current.CancellationToken);
        Assert.Equal(
            FocusAggregator.MaxDailyGoalMinutes,
            await service.GetTargetMinutesAsync(date, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Tracker_records_sessions_and_aggregates_by_local_day()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new FocusSessionRepository(temp.Database);
        var tracker = new FocusTrackerService(store, todos: null, now: () => Now);

        await tracker.RecordSessionAsync(
            Session("today", FocusSource.Pomodoro, Now.AddMinutes(-25), Now, 1500, true),
            TestContext.Current.CancellationToken);

        await tracker.RecordSessionAsync(
            Session(
                "yesterday",
                FocusSource.Timer,
                Now.AddDays(-1).AddMinutes(-30),
                Now.AddDays(-1),
                1800,
                true),
            TestContext.Current.CancellationToken);

        var week = await tracker.AggregateAsync(
            PeriodKind.Week7,
            0,
            TestContext.Current.CancellationToken);

        Assert.Equal(55, week.FocusMinutes);
        Assert.Equal(1, week.PomodoroCount);
        Assert.Equal(new DateOnly(2026, 11, 4), week.Start);
        Assert.Equal(new DateOnly(2026, 11, 10), week.End);
    }

    [Fact]
    public void Recorder_publishes_each_session_once_and_maps_fields()
    {
        var tracker = new CapturingTracker();
        var hub = new FocusSessionHub();
        using var recorder = new FocusSessionRecorder(hub, tracker);

        var id = Guid.NewGuid();
        var session = new FocusSessionEvent(
            id,
            FocusSource.Timer,
            Now.AddSeconds(-90),
            Now,
            TimeSpan.FromSeconds(90),
            Completed: true);

        hub.Publish(session);
        hub.Publish(session);

        var saved = Assert.Single(tracker.Sessions);
        Assert.Equal(id.ToString("N"), saved.SessionKey);
        Assert.Equal(90, saved.DurationSeconds);
        Assert.True(saved.Finished);
    }

    [Fact]
    public void Recorder_swallows_tracker_failures()
    {
        var tracker = new ThrowingTracker();
        var hub = new FocusSessionHub();
        using var recorder = new FocusSessionRecorder(hub, tracker);

        var exception = Record.Exception(() => hub.Publish(new FocusSessionEvent(
            Guid.NewGuid(),
            FocusSource.Pomodoro,
            Now.AddMinutes(-25),
            Now,
            TimeSpan.FromMinutes(25),
            Completed: true)));

        Assert.Null(exception);
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

    private sealed class CapturingTracker : IFocusTracker
    {
        public List<FocusSession> Sessions { get; } = [];

        public Task RecordSessionAsync(FocusSession session, CancellationToken cancellationToken = default)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<PeriodAggregate> AggregateAsync(
            PeriodKind kind,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingTracker : IFocusTracker
    {
        public Task RecordSessionAsync(FocusSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("写入失败");

        public Task<PeriodAggregate> AggregateAsync(
            PeriodKind kind,
            int offset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
