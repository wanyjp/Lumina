using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Services.Configuration;

/// <summary>快捷键动作键名，与原型 `shiguang.hotkeys.v1` 一致。</summary>
public static class HotkeyActions
{
    public const string ShowWindow = "show";
    public const string NewNote = "newnote";
    public const string OpenClipboard = "clipboard";
    public const string TogglePomodoro = "pomodoro";
    public const string RegionCapture = "region";
    public const string FullScreenCapture = "full";
    public const string DelayedCapture = "delay";
    public const string ColorPick = "color";
    public const string CopyColorValue = "copycolor";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        ShowWindow, NewNote, OpenClipboard, TogglePomodoro,
        RegionCapture, FullScreenCapture, DelayedCapture, ColorPick, CopyColorValue,
    };
}

/// <summary>快捷键动作的可读名称；设置中心统一按此展示。</summary>
public sealed record HotkeyActionInfo(string Action, string Title, string Summary);

public static class HotkeyActionCatalog
{
    public static IReadOnlyList<HotkeyActionInfo> All { get; } = new[]
    {
        new HotkeyActionInfo(HotkeyActions.ShowWindow, "唤起主窗口", "从托盘或任意前台恢复拾光"),
        new HotkeyActionInfo(HotkeyActions.NewNote, "新建便签", "新建一条便签并打开编辑器"),
        new HotkeyActionInfo(HotkeyActions.OpenClipboard, "打开剪贴板", "显示/隐藏剪贴板浮窗"),
        new HotkeyActionInfo(HotkeyActions.TogglePomodoro, "开始/暂停番茄", "运行中暂停、暂停恢复，空闲时按设定时长开始"),
        new HotkeyActionInfo(HotkeyActions.RegionCapture, "区域截图", "任意位置框选区域并进入标注编辑器"),
        new HotkeyActionInfo(HotkeyActions.FullScreenCapture, "全屏截图", "截取整个虚拟屏"),
        new HotkeyActionInfo(HotkeyActions.DelayedCapture, "延时截图", "3 秒倒计时后截屏"),
        new HotkeyActionInfo(HotkeyActions.ColorPick, "取色", "截图页聚焦时全屏取色"),
        new HotkeyActionInfo(HotkeyActions.CopyColorValue, "复制颜色值", "截图页聚焦时复制当前色值"),
    };

    public static string Title(string action) =>
        All.FirstOrDefault(info => info.Action == action)?.Title ?? action;
}

/// <summary>9 项默认绑定：全局 7 项 + 模块内 2 项。</summary>
public static class HotkeyCatalog
{
    public static IReadOnlyDictionary<string, HotkeyGesture> Defaults { get; } = new Dictionary<string, HotkeyGesture>
    {
        [HotkeyActions.ShowWindow] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Space", HotkeyScope.Global),
        [HotkeyActions.NewNote] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "N", HotkeyScope.Global),
        [HotkeyActions.OpenClipboard] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "V", HotkeyScope.Global),
        [HotkeyActions.TogglePomodoro] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "P", HotkeyScope.Global),
        [HotkeyActions.RegionCapture] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "A", HotkeyScope.Global),
        [HotkeyActions.FullScreenCapture] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F", HotkeyScope.Global),
        [HotkeyActions.DelayedCapture] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "D", HotkeyScope.Global),
        [HotkeyActions.ColorPick] = new(HotkeyModifiers.Alt | HotkeyModifiers.Shift, "C", HotkeyScope.Module),
        [HotkeyActions.CopyColorValue] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "C", HotkeyScope.Module),
    };

    /// <summary>空绑定：不注册、不参与冲突检测。</summary>
    public static HotkeyGesture Unbound(HotkeyScope scope) =>
        new(HotkeyModifiers.None, string.Empty, scope);

    public static bool IsUnbound(HotkeyGesture gesture) =>
        string.IsNullOrWhiteSpace(gesture.Key);
}


/// <summary>应用元信息；版本随发布更新。</summary>
public static class AppInfo
{
    public const string Name = "拾光";
    public const string EnglishName = "Lumina";
    public const string Version = "1.0.0";

    /// <summary>内部阶段代号：仅用于日志与开发文档，不在正式界面展示。</summary>
    public const string Phase = "P4-08";

    /// <summary>当前阶段标题，用于开发文档与日志。</summary>
    public const string PhaseTitle = "v1.0 正式发布";

    public const string Description = "Windows 桌面聚合效率工具";
}
