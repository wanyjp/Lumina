using Lumina.Domain.Enums;

namespace Lumina.Services.Contracts;

/// <summary>时钟快照；业务层只提供 UTC/本地时间与原始字段，格式化留给显示层。</summary>
public sealed record ClockSnapshot(
    DateTimeOffset Utc,
    DateTimeOffset Local,
    DateOnly Date,
    string? LunarText,
    string? SelectedCityLabel);

/// <summary>世界时钟卡片数据。</summary>
public sealed record WorldClockItem(string IanaId, string DisplayName, DateTimeOffset LocalTime);

/// <summary>计时器状态；以结束时刻或剩余时间为基准，避免累加 tick 漂移。</summary>
public sealed record TimerState(TimeSpan Remaining, bool IsRunning, bool IsPaused)
{
    /// <summary>本次（或下一次）计时的目标时长，供界面计算已过时间与标记。</summary>
    public TimeSpan Target { get; init; }
}

/// <summary>秒表状态：已累计时长与是否运行。</summary>
public sealed record StopwatchState(TimeSpan Elapsed, bool IsRunning, bool IsPaused);

/// <summary>番茄钟状态。</summary>
public sealed record PomodoroState(
    PomodoroPhase Phase,
    TimeSpan Remaining,
    int Cycle,
    int TotalCycles,
    bool Completed = false);

/// <summary>白噪音声源；<c>RelativePath</c> 指向音频库中的文件，<c>Volume</c> 为默认音量。</summary>
public sealed record SoundPreset(
    string Id,
    string DisplayName,
    double Volume,
    string? CategoryId = null,
    string? RelativePath = null);

/// <summary>月历中的跨天条带；P1-08 渲染色条，P1-06 起由日程数据填充。</summary>
public sealed record CalendarBarModel(
    string Title,
    bool IsStart,
    bool IsEnd,
    int Level,
    Priority Priority = Priority.Normal);

/// <summary>月历中的单日模型；`ItemCount` 为去重后的当日条目总数。</summary>
public sealed record CalendarDayModel(
    DateOnly Date,
    string? LunarText,
    bool IsCurrentMonth,
    bool IsToday,
    int ItemCount,
    IReadOnlyList<CalendarBarModel> Bars);

/// <summary>月视图网格模型；由模型构建器生成，视图只负责渲染。</summary>
public sealed record CalendarMonthModel(int Year, int Month, IReadOnlyList<CalendarDayModel> Days);

/// <summary>截图结果；图像以字节数组承载，避免服务层依赖 WPF 图像类型。</summary>
public sealed record CaptureResult(
    CaptureType Type,
    byte[] ImageBytes,
    int Width,
    int Height,
    DateTimeOffset CapturedAt);

/// <summary>可捕获的可见窗口；P2-04 窗口截图枚举结果，供选择器展示。</summary>
public sealed record CapturableWindow(
    nint Handle,
    string Title,
    string ProcessName,
    byte[]? IconPng);

/// <summary>取色结果；统一按 sRGB 读取。</summary>
public sealed record ColorSample(byte R, byte G, byte B, int X, int Y)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>待办完成率统计；用于 P1-09 展示与 P3 统计复用。</summary>
public sealed record TodoCompletionStats(DateOnly Start, DateOnly End, int Completed, int Total)
{
    public double CompletionRate => Total == 0 ? 0 : (double)Completed / Total;
}

/// <summary>统计区间聚合结果。</summary>
public sealed record PeriodAggregate(
    PeriodKind Kind,
    DateOnly Start,
    DateOnly End,
    int FocusMinutes,
    int PomodoroCount,
    int CompletedTodos,
    int TotalTodos);

/// <summary>设置变更通知。</summary>
public sealed record SettingChanged(string Key, object? Value, string? Module);

/// <summary>快捷键组合；修饰键顺序固定 Ctrl → Alt → Shift → Win。</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key, HotkeyScope Scope)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (!string.IsNullOrWhiteSpace(Key)) parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

/// <summary>小组件布局与外观状态；由 WidgetHost 持久化。</summary>
public sealed record WidgetState(
    WidgetKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    double Opacity = 1.0,
    bool Topmost = true,
    bool ClickThrough = false)
{
    /// <summary>组件绑定的数据源 id（倒计时组件使用；其他组件为 null）。</summary>
    public long? SourceId { get; init; }

    /// <summary>组件皮肤；默认亚克力，旧配置缺少该字段时同样落到此值。</summary>
    public WidgetSkin Skin { get; init; } = WidgetSkin.Acrylic;
}

/// <summary>统计周期内单日数据点（P3-02）。</summary>
public sealed record FocusDailyPoint(
    DateOnly Date,
    int FocusMinutes,
    int PomodoroCount,
    int TargetMinutes,
    bool GoalMet);

/// <summary>统计视图一次性快照（P3-02）：本期、上期、逐日序列与今日目标。</summary>
public sealed record FocusStatsSnapshot(
    PeriodAggregate Current,
    PeriodAggregate Previous,
    IReadOnlyList<FocusDailyPoint> Daily,
    int TodayFocusMinutes,
    int TodayTargetMinutes)
{
    /// <summary>本期是否完全没有可展示的数据（用于空态）。</summary>
    public bool IsEmpty =>
        Current.FocusMinutes == 0
        && Current.PomodoroCount == 0
        && Current.TotalTodos == 0
        && Daily.All(point => point.FocusMinutes == 0 && point.PomodoroCount == 0);

    /// <summary>与上期相比的专注时长变化比例；上期为 0 时返回 null。</summary>
    public double? FocusDelta =>
        Previous.FocusMinutes == 0
            ? null
            : (double)(Current.FocusMinutes - Previous.FocusMinutes) / Previous.FocusMinutes;
}
