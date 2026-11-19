using Lumina.Domain.Models;

namespace Lumina.Domain.Clipboard;

/// <summary>
/// 剪贴板保留策略的纯计算口径：上限 <c>0</c> 表示不限制；
/// 固定条目不参与计数，也不会被淘汰；淘汰顺序为更新时间正序、同时间按 id 正序。
/// </summary>
public static class EvictionPolicy
{
    public const int DefaultLimit = 200;
    public const int MinLimit = 0;
    public const int MaxLimit = 9999;

    /// <summary>把任意输入钳制到合法区间；负数按 0（不限制）处理。</summary>
    public static int ClampLimit(int limit) => Math.Clamp(limit, MinLimit, MaxLimit);

    /// <summary>按非固定记录数计算需要淘汰的条数；0 表示不限制。</summary>
    public static int ExcessCount(int nonPinnedCount, int limit)
    {
        var effective = ClampLimit(limit);
        if (effective == MinLimit || nonPinnedCount <= effective)
        {
            return 0;
        }

        return nonPinnedCount - effective;
    }

    /// <summary>从给定记录中选出应淘汰的最旧非固定条目，供单元测试与诊断使用。</summary>
    public static IReadOnlyList<ClipboardItem> SelectForEviction(
        IEnumerable<ClipboardItem> items,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var candidates = items
            .Where(item => !item.Pinned && item.DeletedAt is null)
            .OrderBy(item => item.UpdatedAt)
            .ThenBy(item => item.Id)
            .ToArray();

        var excess = ExcessCount(candidates.Length, limit);
        return excess == 0
            ? Array.Empty<ClipboardItem>()
            : candidates.Take(excess).ToArray();
    }
}
