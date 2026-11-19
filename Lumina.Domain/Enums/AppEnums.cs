namespace Lumina.Domain.Enums;

/// <summary>完成风格四套：描边勾选 / 填充折叠 / 环形进度 / 状态圆点。</summary>
public enum CompletionStyle
{
    Stroke = 0,
    Fill = 1,
    Ring = 2,
    Dot = 3,
}

/// <summary>提醒跳转目标；与模块 id 一一对应。</summary>
public enum ReminderRoute
{
    Schedule = 0,
    Countdown = 1,
}

/// <summary>桌面小组件类型。</summary>
public enum WidgetKind
{
    Clock = 0,
    Schedule = 1,
    Countdown = 2,
    Note = 3,
    Timer = 4,

    /// <summary>秒表小组件（P2-03 后续修复新增，与计时器独立）。</summary>
    Stopwatch = 5,

    /// <summary>番茄钟小组件（P2-03 后续修复新增，与计时器独立）。</summary>
    Pomodoro = 6,
}

/// <summary>
/// 桌面小组件皮肤（P3-04 反馈修复）：不透明、透明、亚克力、液态玻璃。
/// 只影响组件卡片的表面材质，不改动尺寸、拖动与点击穿透行为。
/// </summary>
public enum WidgetSkin
{
    Opaque = 0,
    Transparent = 1,
    Acrylic = 2,
    LiquidGlass = 3,
}

/// <summary>截图类型。</summary>
public enum CaptureType
{
    Region = 0,
    FullScreen = 1,
    Window = 2,
    Delayed = 3,
}

/// <summary>剪贴板条目类型。</summary>
public enum ClipboardKind
{
    Text = 0,
    Link = 1,
    Image = 2,
    Html = 3,
    File = 4,
}

/// <summary>专注会话来源。</summary>
public enum FocusSource
{
    Pomodoro = 0,
    Timer = 1,
}

/// <summary>番茄钟阶段。</summary>
public enum PomodoroPhase
{
    Idle = 0,
    Focus = 1,
    Break = 2,
    Paused = 3,
}

/// <summary>统计周期类型。</summary>
public enum PeriodKind
{
    Week7 = 0,
    Month30 = 1,
    MonthlyArchive = 2,
}

/// <summary>保险库分类。</summary>
public enum VaultCategory
{
    All = 0,
    Login = 1,
    BankCard = 2,
    IdDocument = 3,
    SecureNote = 4,
    Favorite = 5,
}

/// <summary>快捷键作用域：全局或模块内。</summary>
public enum HotkeyScope
{
    Global = 0,
    Module = 1,
}

/// <summary>快捷键修饰键，固定顺序 Ctrl → Alt → Shift → Win。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}
