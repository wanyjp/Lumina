namespace Lumina.Services.Configuration;

/// <summary>设置中心分组：13 组顺序固定，抽屉与中心共用同一分组口径。</summary>
public sealed record SettingsGroup(string Id, string Title, string Summary);

public static class SettingsGroupCatalog
{
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Hotkeys = "hotkeys";
    public const string Clock = "clock";
    public const string Schedule = "schedule";
    public const string Reminders = "reminders";
    public const string Clipboard = "clipboard";
    public const string Capture = "capture";
    public const string Countdown = "countdown";
    public const string Focus = "focus";
    public const string Note = "note";
    public const string Vault = "vault";
    public const string About = "about";

    public static IReadOnlyList<SettingsGroup> All { get; } = new[]
    {
        new SettingsGroup(General, "通用", "启动、常驻与桌面小组件"),
        new SettingsGroup(Appearance, "外观", "主题、完成风格与动效"),
        new SettingsGroup(Hotkeys, "快捷键", "全局 7 项与模块内 2 项"),
        new SettingsGroup(Clock, "时钟", "显示格式、世界时钟与白噪音"),
        new SettingsGroup(Schedule, "日程", "日历布局与每周起始"),
        new SettingsGroup(Reminders, "提醒与通知", "日程提醒与通知通道"),
        new SettingsGroup(Clipboard, "剪贴板", "保留容量、采集范围与隐私"),
        new SettingsGroup(Capture, "截图", "截图保存与标注能力"),
        new SettingsGroup(Countdown, "倒计时", "默认提醒与周期锚点"),
        new SettingsGroup(Focus, "专注统计", "每日目标与会话来源"),
        new SettingsGroup(Note, "便签", "自动保存与标签限制"),
        new SettingsGroup(Vault, "密码管理器", "加密方式与自动锁定"),
        new SettingsGroup(About, "关于与数据", "版本、路径与备份"),
    };

    public static SettingsGroup For(string id) =>
        All.FirstOrDefault(group => group.Id == id) ?? All[0];
}
