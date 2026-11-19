using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Hotkeys;

/// <summary>单个全局快捷键的注册结果。</summary>
public enum HotkeyRegistrationStatus
{
    /// <summary>已注册到系统。</summary>
    Registered = 0,

    /// <summary>空绑定：用户清空，不注册也不参与冲突。</summary>
    Skipped = 1,

    /// <summary>与表内其他动作重复，未注册。</summary>
    Conflict = 2,

    /// <summary>系统拒绝（被其他程序占用或组合不受支持）。</summary>
    Rejected = 3,
}

/// <summary>一条注册明细；失败时 <see cref="ConflictAction"/> 指明占用方。</summary>
public sealed record HotkeyRegistrationEntry(
    string Action,
    HotkeyGesture Gesture,
    HotkeyRegistrationStatus Status,
    string? ConflictAction = null);

/// <summary>一次全量注册的结果，供设置中心与启动日志展示。</summary>
public sealed record HotkeyRegistrationReport(IReadOnlyList<HotkeyRegistrationEntry> Entries)
{
    public int Total => Entries.Count;

    public int RegisteredCount => Entries.Count(entry => entry.Status == HotkeyRegistrationStatus.Registered);

    public IReadOnlyList<HotkeyRegistrationEntry> Failures => Entries
        .Where(entry => entry.Status is HotkeyRegistrationStatus.Conflict or HotkeyRegistrationStatus.Rejected)
        .ToList();

    public bool HasFailures => Failures.Count > 0;

    /// <summary>一句话状态：`5/6 已注册 · 全屏截图 Ctrl + Shift + F 被占用`。</summary>
    public string Summary
    {
        get
        {
            if (Total == 0)
            {
                return "没有需要注册的全局快捷键";
            }

            var text = $"{RegisteredCount}/{Total} 已注册";
            if (!HasFailures)
            {
                return text + " · 全部可用";
            }

            var first = Failures[0];
            var reason = first.Status == HotkeyRegistrationStatus.Conflict
                ? $"与「{HotkeyActionCatalog.Title(first.ConflictAction ?? string.Empty)}」重复"
                : "被其他程序占用";

            return $"{text} · {HotkeyActionCatalog.Title(first.Action)} {first.Gesture} {reason}";
        }
    }
}

/// <summary>
/// 全局快捷键注册编排：启动、设置修改、解锁/唤醒后统一走这里。
/// <para>
/// 职责边界：本类只做“表内冲突检测 + 调用系统注册 + 汇总结果”，
/// 不写配置；注册失败时保留用户原有绑定（清空/回滚由调用方决定）。
/// 模块内快捷键（取色、复制色值）不在此注册，由截图模块在获得焦点时匹配。
/// </para>
/// </summary>
public sealed class HotkeyRegistrationService
{
    private readonly IHotkeyManager _manager;
    private readonly HotkeyBindingService _bindings;

    public HotkeyRegistrationService(IHotkeyManager manager, HotkeyBindingService bindings)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(bindings);

        _manager = manager;
        _bindings = bindings;
    }

    /// <summary>已经接入处理逻辑的全局动作；P3-06 起包含「新建便签」。</summary>
    public static IReadOnlyList<string> ActiveGlobalActions { get; } =
    [
        HotkeyActions.ShowWindow,
        HotkeyActions.NewNote,
        HotkeyActions.OpenClipboard,
        HotkeyActions.TogglePomodoro,
        HotkeyActions.RegionCapture,
        HotkeyActions.FullScreenCapture,
        HotkeyActions.DelayedCapture,
    ];

    public static bool IsActiveGlobalAction(string action) =>
        ActiveGlobalActions.Contains(action, StringComparer.Ordinal);

    /// <summary>是否为已接入的模块内动作（只在对应模块聚焦时生效）。</summary>
    public static bool IsModuleAction(string action) =>
        HotkeyCatalog.Defaults.TryGetValue(action, out var gesture) && gesture.Scope == HotkeyScope.Module;

    /// <summary>
    /// 注册全部已接入的全局动作。重复调用是安全的（同组合会直接命中已注册项），
    /// 用于解锁、睡眠唤醒后的状态校验与补救。
    /// </summary>
    public HotkeyRegistrationReport RegisterAll()
    {
        var bindings = _bindings.Bindings();
        var entries = new List<HotkeyRegistrationEntry>(ActiveGlobalActions.Count);
        var accepted = new List<(string Action, HotkeyGesture Gesture)>();

        foreach (var action in ActiveGlobalActions)
        {
            var gesture = bindings.GetValueOrDefault(action, HotkeyCatalog.Unbound(HotkeyScope.Global));

            if (HotkeyCatalog.IsUnbound(gesture))
            {
                entries.Add(new HotkeyRegistrationEntry(action, gesture, HotkeyRegistrationStatus.Skipped));
                continue;
            }

            // 先做表内冲突检测：同一个组合只让排在前面的动作注册。
            var duplicate = accepted.FirstOrDefault(
                pair => HotkeyConflictDetector.Matches(pair.Gesture, gesture));

            if (duplicate.Action is not null)
            {
                entries.Add(new HotkeyRegistrationEntry(
                    action,
                    gesture,
                    HotkeyRegistrationStatus.Conflict,
                    duplicate.Action));
                continue;
            }

            var registered = _manager.Register(action, gesture);
            entries.Add(new HotkeyRegistrationEntry(
                action,
                gesture,
                registered ? HotkeyRegistrationStatus.Registered : HotkeyRegistrationStatus.Rejected));

            if (registered)
            {
                accepted.Add((action, gesture));
            }
        }

        return new HotkeyRegistrationReport(entries);
    }

    /// <summary>
    /// 重新注册单个动作（设置中心保存后调用）。返回 false 时 <paramref name="conflictAction"/>
    /// 可能是表内冲突方；为 null 表示被系统或其他程序占用。
    /// </summary>
    public bool TryRegister(string action, HotkeyGesture gesture, out string? conflictAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        conflictAction = HotkeyConflictDetector.FindConflict(_bindings.Bindings(), action, gesture);
        if (conflictAction is not null)
        {
            return false;
        }

        return !HotkeyCatalog.IsUnbound(gesture) && _manager.Register(action, gesture);
    }

    /// <summary>退出、注销前注销全部热键，避免系统内残留注册。</summary>
    public void UnregisterAll() => _manager.UnregisterAll();
}
