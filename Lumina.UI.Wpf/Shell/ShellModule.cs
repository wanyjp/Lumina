namespace Lumina.UI.Wpf.Shell;

/// <summary>主窗口左侧导航的模块定义。</summary>
public sealed record ShellModule(
    string Id,
    string Title,
    string Summary,
    bool IsSettings = false);

/// <summary>九大模块 + 设置入口。</summary>
public static class ShellModuleCatalog
{
    public static ShellModule Settings { get; } = new(
        "settings",
        "设置",
        "主题、通用、快捷键、剪贴板、模块与关于",
        IsSettings: true);

    public static IReadOnlyList<ShellModule> All { get; } = new[]
    {
        new ShellModule(
            "clock",
            "时钟",
            "时钟、世界时钟、计时器、秒表、番茄与白噪音"),
        new ShellModule(
            "schedule",
            "日历日程",
            "月历、日程时间轴、提醒与今日安排"),
        new ShellModule(
            "todo",
            "待办",
            "待办清单、完成风格、筛选与排序"),
        new ShellModule(
            "clipboard",
            "剪贴板",
            "文本/链接/图片/HTML/文件历史与隐私排除"),
        new ShellModule(
            "capture",
            "截图取色",
            "区域/全屏/延时截图、取色与 OCR"),
        new ShellModule(
            "countdown",
            "倒计时",
            "纪念日、周期锚点、紧迫分档与小组件"),
        new ShellModule(
            "focus",
            "专注统计",
            "专注会话、每日目标、热力图与周期翻页"),
        new ShellModule(
            "note",
            "便签",
            "速记、放大滚动、转待办与便签小组件"),
        new ShellModule(
            "vault",
            "密码管理器",
            "本地加密保险库、分类、锁定与安全复制"),
        Settings,
    };
}
