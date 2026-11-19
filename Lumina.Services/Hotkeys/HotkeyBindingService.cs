using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Hotkeys;

/// <summary>快捷键保存结果；冲突与非法组合不写入配置。</summary>
public sealed record HotkeyBindingResult(bool Saved, string? ConflictAction, string? Message)
{
    public static HotkeyBindingResult Ok(string message = "已保存") => new(true, null, message);

    public static HotkeyBindingResult Invalid(string message) => new(false, null, message);

    public static HotkeyBindingResult Conflict(string action, string message) =>
        new(false, action, message);
}

/// <summary>
/// 统一快捷键表：默认值来自 <see cref="HotkeyCatalog"/>，用户修改写入
/// `hotkey.&lt;action&gt;` 键；空绑定保存为空手势，不参与冲突检测。
/// </summary>
public sealed class HotkeyBindingService
{
    public const string KeyPrefix = "hotkey.";

    private readonly ISettingsStore _settings;

    public HotkeyBindingService(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public static string KeyFor(string action) => KeyPrefix + action;

    /// <summary>有效绑定 = 默认值 + 已保存覆盖；被清空的动作返回空绑定。</summary>
    public IReadOnlyDictionary<string, HotkeyGesture> Bindings()
    {
        var bindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);

        foreach (var (action, fallback) in HotkeyCatalog.Defaults)
        {
            var stored = _settings.Get<HotkeyGesture>(KeyFor(action));
            bindings[action] = stored ?? fallback;
        }

        return bindings;
    }

    public HotkeyGesture Get(string action)
    {
        EnsureKnown(action);

        var stored = _settings.Get<HotkeyGesture>(KeyFor(action));
        return stored ?? HotkeyCatalog.Defaults[action];
    }

    /// <summary>保存新组合；空键视为清空，修饰键缺失或全表冲突时拒绝。</summary>
    public HotkeyBindingResult Set(string action, HotkeyGesture gesture)
    {
        EnsureKnown(action);

        var scope = HotkeyCatalog.Defaults[action].Scope;
        var normalized = gesture with { Scope = scope };

        if (HotkeyCatalog.IsUnbound(normalized))
        {
            return Clear(action);
        }

        if (normalized.Modifiers == HotkeyModifiers.None)
        {
            return HotkeyBindingResult.Invalid("快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win）。");
        }

        var conflict = HotkeyConflictDetector.FindConflict(Bindings(), action, normalized);
        if (conflict is not null)
        {
            return HotkeyBindingResult.Conflict(
                conflict,
                $"与「{HotkeyActionCatalog.Title(conflict)}」冲突，未保存。");
        }

        _settings.Set(KeyFor(action), normalized, module: "hotkeys");
        return HotkeyBindingResult.Ok();
    }

    public HotkeyBindingResult Clear(string action)
    {
        EnsureKnown(action);

        var scope = HotkeyCatalog.Defaults[action].Scope;
        _settings.Set(KeyFor(action), HotkeyCatalog.Unbound(scope), module: "hotkeys");
        return HotkeyBindingResult.Ok("已清空");
    }

    public void Reset(string action)
    {
        EnsureKnown(action);
        _settings.Set(KeyFor(action), HotkeyCatalog.Defaults[action], module: "hotkeys");
    }

    public void ResetAll()
    {
        foreach (var (action, gesture) in HotkeyCatalog.Defaults)
        {
            _settings.Set(KeyFor(action), gesture, module: "hotkeys");
        }
    }

    public bool IsUnbound(string action) => HotkeyCatalog.IsUnbound(Get(action));

    private static void EnsureKnown(string action)
    {
        if (!HotkeyCatalog.Defaults.ContainsKey(action))
        {
            throw new ArgumentException($"未知快捷键动作：{action}", nameof(action));
        }
    }
}
