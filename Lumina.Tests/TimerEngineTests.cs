using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P1-03 计时器、秒表与番茄钟状态机。</summary>
public class TimerEngineTests
{
    [Fact]
    public void Timer_uses_end_time_and_pause_keeps_remaining()
    {
        var clock = new FakeClock();
        using var timer = new TimerEngine(new FocusSessionHub(), () => clock.Now);

        timer.Start(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(8), timer.State.Remaining);

        timer.Pause();
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(8), timer.State.Remaining);
        Assert.True(timer.State.IsPaused);

        timer.Resume();
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.State.Remaining);
        Assert.True(timer.State.IsRunning);
    }

    [Fact]
    public void Timer_reset_restores_target()
    {
        var clock = new FakeClock();
        using var timer = new TimerEngine(new FocusSessionHub(), () => clock.Now);

        timer.Start(TimeSpan.FromMinutes(25));
        clock.Advance(TimeSpan.FromMinutes(25));
        timer.Reset();

        Assert.Equal(TimeSpan.FromMinutes(25), timer.State.Remaining);
        Assert.False(timer.State.IsRunning);
    }

    [Fact]
    public void Marks_are_recorded_on_the_engine_and_cleared_by_reset()
    {
        var clock = new FakeClock();
        using var timer = new TimerEngine(new FocusSessionHub(), () => clock.Now);

        // 空闲状态标记不产生记录。
        timer.Mark();
        Assert.Empty(timer.Marks);

        timer.Start(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(2));
        timer.Mark();
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.Mark();

        // 主界面与小组件读同一份标记，按已过时间累计。
        Assert.Equal(
            [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)],
            timer.Marks);

        // 暂停时也能标记，且不改变计时状态。
        timer.Pause();
        timer.Mark();
        Assert.Equal(3, timer.Marks.Count);
        Assert.True(timer.State.IsPaused);

        timer.Reset();
        Assert.Empty(timer.Marks);
    }

    [Fact]
    public void Starting_a_new_run_clears_previous_marks()
    {
        var clock = new FakeClock();
        using var timer = new TimerEngine(new FocusSessionHub(), () => clock.Now);

        timer.Start(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.Mark();
        Assert.Single(timer.Marks);

        timer.Start(TimeSpan.FromMinutes(5));
        Assert.Empty(timer.Marks);
    }

    [Fact]
    public void Timer_publishes_one_session_when_finished()
    {
        var hub = new FocusSessionHub();
        var sessions = new List<FocusSessionEvent>();
        hub.Published += sessions.Add;

        using var timer = new TimerEngine(hub);
        timer.Start(TimeSpan.FromMilliseconds(120));

        // 计时器 tick 为 200ms；并行测试满载时线程池回调可能被推迟，等待窗口留足余量。
        Assert.True(SpinWait.SpinUntil(() => sessions.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.Single(sessions);
        Assert.Equal(FocusSource.Timer, sessions[0].Source);
        Assert.True(sessions[0].Completed);
        Assert.Equal(TimeSpan.Zero, timer.State.Remaining);
        Assert.False(timer.State.IsRunning);
    }

    [Fact]
    public void Hub_ignores_duplicate_session_ids()
    {
        var hub = new FocusSessionHub();
        var count = 0;
        hub.Published += _ => count++;

        var sessionId = Guid.NewGuid();
        var session = new FocusSessionEvent(
            sessionId,
            FocusSource.Pomodoro,
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            TimeSpan.FromMinutes(25),
            Completed: true);

        hub.Publish(session);
        hub.Publish(session);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Stopwatch_accumulates_laps_and_resets()
    {
        var clock = new FakeClock();
        var stopwatch = new StopwatchEngine(() => clock.Now);

        stopwatch.Start();
        clock.Advance(TimeSpan.FromSeconds(1.5));
        stopwatch.Pause();
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(1.5), stopwatch.State.Elapsed);

        stopwatch.Start();
        clock.Advance(TimeSpan.FromSeconds(0.25));
        stopwatch.MarkLap();
        Assert.Equal(TimeSpan.FromSeconds(1.75), stopwatch.State.Elapsed);
        Assert.Single(stopwatch.Laps);

        stopwatch.Reset();
        Assert.Equal(TimeSpan.Zero, stopwatch.State.Elapsed);
        Assert.Empty(stopwatch.Laps);
        Assert.False(stopwatch.State.IsRunning);
    }

    [Fact]
    public void Pomodoro_runs_focus_break_cycles_and_publishes_focus_only()
    {
        var clock = new FakeClock();
        var hub = new FocusSessionHub();
        var sessions = new List<FocusSessionEvent>();
        hub.Published += sessions.Add;

        using var pomodoro = new PomodoroService(hub, () => clock.Now);
        pomodoro.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), 2);

        // 引擎按 200ms 轮询假时钟；全量测试并行时线程池可能排队，
        // 给 8 秒余量避免把调度抖动误判为状态机缺陷。
        var settle = TimeSpan.FromSeconds(8);

        Assert.Equal(PomodoroPhase.Focus, pomodoro.Current.Phase);
        Assert.Equal(1, pomodoro.Current.Cycle);

        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(SpinWait.SpinUntil(
            () => pomodoro.Current.Phase == PomodoroPhase.Break,
            settle));
        Assert.Single(sessions);
        Assert.Equal(TimeSpan.FromMinutes(25), sessions[0].Duration);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(SpinWait.SpinUntil(
            () => pomodoro.Current.Phase == PomodoroPhase.Focus && pomodoro.Current.Cycle == 2,
            settle));

        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(SpinWait.SpinUntil(
            () => pomodoro.Current.Phase == PomodoroPhase.Idle && sessions.Count == 2,
            settle));
        Assert.Equal(2, sessions.Count);

        // 全部轮次完成后回到“可再次开始”的待开始态：显示用户设定的专注时长，
        // 而不是 00:00:00，并带上 Completed 供界面展示完成文案。
        var completed = pomodoro.Current;
        Assert.True(completed.Completed);
        Assert.Equal(TimeSpan.FromMinutes(25), completed.Remaining);
        Assert.Equal(2, completed.TotalCycles);
    }

    [Fact]
    public void Pomodoro_pause_freezes_remaining_and_force_stop_reports_partial()
    {
        var clock = new FakeClock();
        var hub = new FocusSessionHub();
        var sessions = new List<FocusSessionEvent>();
        hub.Published += sessions.Add;

        using var pomodoro = new PomodoroService(hub, () => clock.Now);
        pomodoro.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), 4);

        clock.Advance(TimeSpan.FromMinutes(10));
        pomodoro.Pause();
        Assert.Equal(PomodoroPhase.Paused, pomodoro.Current.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), pomodoro.Current.Remaining);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromMinutes(15), pomodoro.Current.Remaining);

        pomodoro.Resume();
        Assert.Equal(PomodoroPhase.Focus, pomodoro.Current.Phase);

        pomodoro.ForceStop();
        Assert.Equal(PomodoroPhase.Idle, pomodoro.Current.Phase);
        Assert.Single(sessions);
        Assert.False(sessions[0].Completed);
        Assert.Equal(TimeSpan.FromMinutes(10), sessions[0].Duration);

        // 重置同样回到可再次开始的待开始态：显示设定时长，不展示 00:00:00。
        Assert.False(pomodoro.Current.Completed);
        Assert.Equal(TimeSpan.FromMinutes(25), pomodoro.Current.Remaining);
    }

    /// <summary>计时器状态需暴露目标时长，供页面与小组件计算已过时间与标记。</summary>
    [Fact]
    public void Timer_state_exposes_target_for_elapsed_calculation()
    {
        var clock = new FakeClock();
        var hub = new FocusSessionHub();
        using var timer = new TimerEngine(hub, () => clock.Now);

        timer.Start(TimeSpan.FromMinutes(40));
        Assert.Equal(TimeSpan.FromMinutes(40), timer.State.Target);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(40), timer.State.Target);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.State.Remaining);

        timer.Pause();
        Assert.Equal(TimeSpan.FromMinutes(40), timer.State.Target);
    }

    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(8));

        public DateTimeOffset Now => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
