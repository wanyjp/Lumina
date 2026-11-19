using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Tests;

/// <summary>
/// P1-14 M1 接口冻结守卫：领域存储与服务层公开接口在 M1 完成后冻结；
/// 任何增删都必须经变更控制并同步更新本测试与冻结文档。
/// P2-01 经变更控制追加 IClipboardStore（仅新增，不修改既有接口）。
/// P2-02 经变更控制扩展 IClipboardStore 成员（接口数量仍为 7）。
/// P2-03 经变更控制追加 IClipboardWriter、扩展 IClipboardStore 与 IClipboardMonitor。
/// P2-03 反馈修复经变更控制扩展 WidgetKind 稳定键与 TimerState 模型（均为纯新增）。
/// P2-08 反馈修复经变更控制为 ITimerEngine 增加 Marks / Mark（标记与主界面共享，接口数量仍为 30）。
/// P3-01 经变更控制追加 IDailyGoalService、IFocusSessionStore、IDailyGoalStore（仅新增）。
/// P3-02 经变更控制追加 IFocusStatsService（仅新增）。
/// P3-03 经变更控制扩展 ICountdownStore（Find/Insert/Update/Delete）与 ICountdownService
/// （AddOrUpdateAsync 返回实体，新增 DeleteAsync 与 Changed；接口数量不变）。
/// P3-05 经变更控制追加 INoteStore，并扩展 INoteService（SaveAsync 返回实体、新增 DeleteAsync 与 Changed）。
/// P3-09 经变更控制追加 ISecureClipboard 与 ISecureClipboardChannel（仅新增），并扩展 IVaultService
/// （ClearClipboardSeconds / SetClearClipboardSecondsAsync）。
/// P4-08 反馈修复经变更控制扩展 IScreenshotStore：Recent / EnforceLimit 的上限改为可空
/// （不传即读取用户配置），并新增 RetentionLimit；接口数量不变。
/// </summary>
public class InterfaceFreezeTests
{
    private static readonly string[] FrozenServiceContracts =
    [
        "IAutoStartupService", "IBackupService", "ICalendarService", "IClipboardMonitor",
        "IClipboardWriter", "IClockService", "IColorProbe", "ICompletionStyleStrategy",
        "ICountdownService", "ICryptoProvider", "IDailyGoalService", "IFocusStatsService",
        "IFocusTracker", "IHotkeyManager",
        "INoiseAudioOutput", "INoiseMixer", "INoteService", "INotifier", "IOcrService",
        "IPomodoroService", "IReminderSource", "IScheduleService", "IScreenCaptureService",
        "IScreenshotStore", "ISecureClipboard", "ISecureClipboardChannel", "ISettingsStore",
        "IStopwatchEngine", "ISystemThemeSource", "ITimerEngine",
        "ITodoService", "IVaultService", "IWidgetHost", "IWorldClockService",
    ];

    private static readonly string[] FrozenStoreContracts =
    [
        "IClipboardStore", "ICountdownStore", "IDailyGoalStore", "IFocusSessionStore",
        "INoteStore", "IReminderDedupStore", "IScheduleStore",
        "IScreenshotCatalog", "ISettingsBackend", "ITodoStore", "IVaultStore", "IWorldCityStore",
    ];

    [Fact]
    public void Public_service_interfaces_are_frozen_at_m1()
    {
        var actual = typeof(ISettingsStore).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.IsInterface
                && type.Namespace == "Lumina.Services.Contracts")
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(FrozenServiceContracts.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Public_store_interfaces_are_frozen_at_m1()
    {
        var actual = typeof(ITodoStore).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.IsInterface
                && type.Namespace == "Lumina.Domain.Stores")
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(FrozenStoreContracts.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Frozen_contract_models_exist_and_are_public()
    {
        var types = new[]
            {
                typeof(SettingChanged).Assembly,
                typeof(TodoItem).Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in new[]
                 {
                     "WidgetState", "HotkeyGesture", "SettingChanged",
                     "ReminderCandidate", "ReminderNotification", "CalendarMonthModel",
                     "TodoItem", "ScheduleEvent", "CountdownItem",
                 })
        {
            Assert.Contains(name, types);
        }
    }

    [Fact]
    public void Widget_kind_stable_keys_are_frozen()
    {
        // 小组件实例键会被持久化到设置后端；既有数值不得变动，否则旧数据会错位。
        Assert.Equal(0, (int)WidgetKind.Clock);
        Assert.Equal(1, (int)WidgetKind.Schedule);
        Assert.Equal(2, (int)WidgetKind.Countdown);
        Assert.Equal(3, (int)WidgetKind.Note);
        Assert.Equal(4, (int)WidgetKind.Timer);

        // P2-03 反馈修复新增：计时器 / 秒表 / 番茄钟各自独立的小组件。
        Assert.Equal(5, (int)WidgetKind.Stopwatch);
        Assert.Equal(6, (int)WidgetKind.Pomodoro);
    }

    [Fact]
    public void Timer_state_target_is_additive()
    {
        var state = new TimerState(TimeSpan.FromMinutes(25), IsRunning: true, IsPaused: false)
        {
            Target = TimeSpan.FromMinutes(25),
        };

        Assert.Equal(TimeSpan.FromMinutes(25), state.Target);
        Assert.Equal(TimeSpan.FromMinutes(25), state.Remaining);
        Assert.True(state.IsRunning);
        Assert.False(state.IsPaused);
    }
}
