using Lumina.Services.Contracts;

namespace Lumina.Services.Configuration;

/// <summary>
/// 快捷键冲突判定：只比较规范化后的完整组合键（修饰键 + 主键），
/// 不比较作用域；同一动作重新绑定时不与自己冲突。
/// </summary>
public static class HotkeyConflictDetector
{
    public static bool Matches(HotkeyGesture first, HotkeyGesture second) =>
        first.Modifiers == second.Modifiers
        && string.Equals(first.Key, second.Key, StringComparison.OrdinalIgnoreCase);

    public static string? FindConflict(
        IReadOnlyDictionary<string, HotkeyGesture> bindings,
        string action,
        HotkeyGesture gesture)
    {
        if (HotkeyCatalog.IsUnbound(gesture))
        {
            return null;
        }

        foreach (var (existingAction, existingGesture) in bindings)
        {
            if (HotkeyCatalog.IsUnbound(existingGesture))
            {
                continue;
            }

            if (!string.Equals(existingAction, action, StringComparison.Ordinal)
                && Matches(existingGesture, gesture))
            {
                return existingAction;
            }
        }

        return null;
    }
}
