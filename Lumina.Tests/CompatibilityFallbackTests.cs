using Lumina.Data.Reminders;
using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Services.Compatibility;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Diagnostics;
using Lumina.Services.Reminders;
using Lumina.Services.Settings;
using Lumina.Services.Widgets;

namespace Lumina.Tests;

/// <summary>
/// P4-03 兼容与兜底：启动首帧看门狗、唤醒补扫、系统档位分类、组件边界收敛。
/// 全部使用假时钟/内存存储，不依赖真实睡眠或显示器。
/// </summary>
public class CompatibilityFallbackTests
{
    private static readonly DateTimeOffset Now = LocalAt(2026, 11, 15, 14, 0);

    // ---------- 启动首帧看门狗 ----------

    [Fact]
    public void Watchdog_marks_first_frame_within_budget()
    {
        var clock = new FakeClock(Now);
        var core = new StartupWatchdogCore(TimeSpan.FromSeconds(10), now: clock.Now);

        core.Start();
        clock.Advance(TimeSpan.FromMilliseconds(1650));

        var signal = core.ObserveFrame();

        Assert.Equal(StartupWatchdogState.Rendered, signal.State);
        Assert.Equal(0, signal.Attempt);
        Assert.False(signal.RecoveredAfterFallback);
        Assert.False(core.UsedFallback);
        Assert.InRange(signal.Elapsed.TotalMilliseconds, 1600, 1700);
        Assert.Null(core.ObserveTick());
    }

    [Fact]
    public void Watchdog_degrades_then_recovers_after_software_fallback()
    {
        var clock = new FakeClock(Now);
        var core = new StartupWatchdogCore(TimeSpan.FromSeconds(10), now: clock.Now);

        core.Start();
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Null(core.ObserveTick());

        clock.Advance(TimeSpan.FromSeconds(1));
        var degraded = core.ObserveTick();

        Assert.NotNull(degraded);
        Assert.Equal(StartupWatchdogState.Degraded, degraded.State);
        Assert.Equal(1, degraded.Attempt);
        Assert.True(core.UsedFallback);

        clock.Advance(TimeSpan.FromSeconds(2));
        var recovered = core.ObserveFrame();

        Assert.Equal(StartupWatchdogState.Rendered, recovered.State);
        Assert.True(recovered.RecoveredAfterFallback);
    }

    [Fact]
    public void Watchdog_reports_failure_when_recovery_budget_expires()
    {
        var clock = new FakeClock(Now);
        var core = new StartupWatchdogCore(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10),
            clock.Now);

        core.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(StartupWatchdogState.Degraded, core.ObserveTick()?.State);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Null(core.ObserveTick());

        clock.Advance(TimeSpan.FromSeconds(1));
        var failed = core.ObserveTick();

        Assert.NotNull(failed);
        Assert.Equal(StartupWatchdogState.Failed, failed.State);
        Assert.Equal(2, failed.Attempt);
        Assert.Null(core.ObserveTick());

        // 失败后首帧仍可能迟到：必须收敛到 Rendered 并标注恢复，便于日志记录“最终可见”。
        clock.Advance(TimeSpan.FromSeconds(3));
        var late = core.ObserveFrame();

        Assert.Equal(StartupWatchdogState.Rendered, late.State);
        Assert.Equal(2, late.Attempt);
        Assert.True(late.RecoveredAfterFallback);
    }

    // ---------- 唤醒/解锁恢复 ----------

    [Fact]
    public async Task Resume_after_long_sleep_rescans_once_within_window()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var scheduler = new ReminderScheduler(
            [new FakeReminderSource(Candidate("sched:9:2026-11-15:m15", Now.AddHours(2).AddMinutes(-1)))],
            new ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        // 构造时心跳记录为 Now；推进 2 小时模拟休眠，心跳期间没有执行。
        var clock = new FakeClock(Now);
        var coordinator = new SystemResumeCoordinator(scheduler, clock.Now);
        clock.Advance(TimeSpan.FromHours(2));

        var outcome = await coordinator.HandleResumeAsync("唤醒", TestContext.Current.CancellationToken);

        Assert.True(outcome.RemindersRescanned);
        Assert.Equal(1, outcome.RemindersFired);
        Assert.Equal(TimeSpan.FromHours(2), outcome.DormantFor);

        var second = await coordinator.HandleResumeAsync("解锁", TestContext.Current.CancellationToken);

        Assert.False(second.RemindersRescanned);
        Assert.Equal(0, second.RemindersFired);
    }

    [Fact]
    public async Task Resume_does_not_backfill_reminders_outside_window()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var scheduler = new ReminderScheduler(
            [new FakeReminderSource(Candidate("sched:9:2026-11-15:m15", Now.AddHours(-3)))],
            new ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        var clock = new FakeClock(Now);
        var coordinator = new SystemResumeCoordinator(scheduler, clock.Now);
        clock.Advance(TimeSpan.FromHours(2));

        var outcome = await coordinator.HandleResumeAsync("唤醒", TestContext.Current.CancellationToken);

        Assert.True(outcome.RemindersRescanned);
        Assert.Equal(0, outcome.RemindersFired);
    }

    [Fact]
    public async Task Resume_is_debounced_for_short_dormancy()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        var scheduler = new ReminderScheduler(
            [new FakeReminderSource(Candidate("sched:9:2026-11-15:ontime", Now))],
            new ReminderDedupRepository(temp.Database),
            settings,
            new FakeNotifier(false));

        var clock = new FakeClock(Now);
        var coordinator = new SystemResumeCoordinator(scheduler, clock.Now);
        clock.Advance(TimeSpan.FromSeconds(20));

        var outcome = await coordinator.HandleResumeAsync("解锁", TestContext.Current.CancellationToken);

        Assert.False(outcome.RemindersRescanned);
        Assert.Equal(0, outcome.RemindersFired);
        Assert.Equal(TimeSpan.FromSeconds(20), outcome.DormantFor);
    }

    // ---------- 系统档位与降级说明 ----------

    [Theory]
    [InlineData(19045, WindowsCompatibilityTier.Windows10)]
    [InlineData(22000, WindowsCompatibilityTier.Windows11)]
    [InlineData(22631, WindowsCompatibilityTier.Windows11)]
    [InlineData(26100, WindowsCompatibilityTier.Windows11_24H2Plus)]
    [InlineData(18363, WindowsCompatibilityTier.Unsupported)]
    public void Compatibility_tier_matches_build_number(int build, WindowsCompatibilityTier expected)
    {
        Assert.Equal(expected, CompatibilityAssessor.Classify(build));
    }

    [Fact]
    public void Compatibility_reports_no_degradation_on_supported_single_display()
    {
        var notes = CompatibilityAssessor.EvaluateDegradations(
            WindowsCompatibilityTier.Windows11_24H2Plus,
            DpiAwarenessMode.PerMonitorV2,
            [new DisplaySnapshot(@"\\.\DISPLAY1", 0, 0, 2560, 1440, 144, true)],
            ocrAvailable: true,
            audioAvailable: true,
            wgcAvailable: true);

        Assert.Empty(notes);
    }

    [Fact]
    public void Compatibility_flags_windows10_and_mixed_dpi_and_missing_capabilities()
    {
        var notes = CompatibilityAssessor.EvaluateDegradations(
            WindowsCompatibilityTier.Windows10,
            DpiAwarenessMode.System,
            [
                new DisplaySnapshot(@"\\.\DISPLAY1", 0, 0, 1920, 1080, 96, true),
                new DisplaySnapshot(@"\\.\DISPLAY2", 1920, 0, 2560, 1440, 144, false),
            ],
            ocrAvailable: false,
            audioAvailable: false,
            wgcAvailable: false);

        Assert.Contains(notes, note => note.Contains("Windows 10", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("DPI 感知为 System", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("混合 DPI", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("OCR 不可用", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("无音频输出设备", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("窗口捕获不可用", StringComparison.Ordinal));
        Assert.Equal(6, notes.Count);
    }

    [Fact]
    public void Snapshot_describes_display_scale_and_summary()
    {
        var snapshot = new CompatibilitySnapshot(
            "Windows 11",
            26100,
            WindowsCompatibilityTier.Windows11_24H2Plus,
            DpiAwarenessMode.PerMonitorV2,
            [new DisplaySnapshot(@"\\.\DISPLAY1", 0, 0, 2560, 1440, 144, true)],
            true,
            "zh-Hans-CN",
            true,
            "扬声器",
            true,
            []);

        Assert.Equal(150, snapshot.Displays[0].ScalePercent);
        Assert.Contains("150%", snapshot.DisplaySummary, StringComparison.Ordinal);
        Assert.Contains("Per-Monitor V2", snapshot.DpiAwarenessLabel, StringComparison.Ordinal);
        Assert.Contains("Windows 11 24H2+", snapshot.Describe(), StringComparison.Ordinal);
    }

    // ---------- 小组件位置收敛 ----------

    [Fact]
    public void Placement_keeps_widget_inside_visible_area()
    {
        var (left, top) = WidgetPlacement.Clamp(
            100,
            100,
            240,
            160,
            new PlacementArea(0, 0, 1920, 1080));

        Assert.Equal(100, left);
        Assert.Equal(100, top);
    }

    [Fact]
    public void Placement_supports_negative_monitor_origin()
    {
        var (left, top) = WidgetPlacement.Clamp(
            -1000,
            200,
            240,
            160,
            new PlacementArea(-1920, 0, 1920, 1080));

        Assert.Equal(-1000, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void Placement_pulls_widget_back_from_offscreen_edges()
    {
        var (left, top) = WidgetPlacement.Clamp(
            1800,
            1000,
            240,
            160,
            new PlacementArea(0, 0, 1920, 1080));

        Assert.Equal(1680, left);
        Assert.Equal(920, top);
    }

    [Fact]
    public void Placement_pins_oversized_widget_to_top_left()
    {
        var (left, top) = WidgetPlacement.Clamp(
            -2500,
            -80,
            900,
            700,
            new PlacementArea(-1920, 0, 800, 600));

        Assert.Equal(-1920, left);
        Assert.Equal(0, top);
    }

    // ---------- 基础设施 ----------

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));

    private static ReminderCandidate Candidate(string key, DateTimeOffset trigger) =>
        new(
            key,
            ReminderRoute.Schedule,
            9,
            DateOnly.FromDateTime(trigger.Date),
            ReminderOffset.Minutes15,
            trigger,
            trigger.AddMinutes(15),
            "唤醒补扫",
            "验证休眠后补扫窗口");

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class FakeClock(DateTimeOffset start)
    {
        private DateTimeOffset _value = start;

        public DateTimeOffset Now() => _value;

        public void Advance(TimeSpan delta) => _value += delta;
    }

    private sealed class FakeReminderSource(ReminderCandidate candidate) : IReminderSource
    {
        public Task<IReadOnlyList<ReminderCandidate>> CandidatesAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderCandidate>>(
                candidate.TriggerAt >= from && candidate.TriggerAt <= to
                    ? [candidate]
                    : []);
    }

    private sealed class FakeNotifier(bool result) : INotifier
    {
        public Task<bool> ShowToastAsync(
            string title,
            string body,
            string? route = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
