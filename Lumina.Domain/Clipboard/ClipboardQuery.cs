using Lumina.Domain.Models;

namespace Lumina.Domain.Clipboard;

/// <summary>一次列表查询的结果：可见条目、页签计数与可选标签。</summary>
public sealed record ClipboardListView
{
    public static ClipboardListView Empty { get; } = new()
    {
        Items = Array.Empty<ClipboardItem>(),
        Counts = new ClipboardCounts(),
        Tags = Array.Empty<string>(),
        TotalCount = 0,
        PinnedCount = 0,
    };

    /// <summary>经页签、搜索与标签筛选后的可见条目，保持仓储返回的固定优先顺序。</summary>
    public required IReadOnlyList<ClipboardItem> Items { get; init; }

    /// <summary>页签计数：以「搜索命中集合」为基数，不受页签与标签筛选影响。</summary>
    public required ClipboardCounts Counts { get; init; }

    /// <summary>全部标签（按使用次数降序、同次数按字典序），供筛选 chip 使用。</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>未删除条目总数，用于「无记录」与「筛选为空」的区分。</summary>
    public required int TotalCount { get; init; }

    public required int PinnedCount { get; init; }
}

/// <summary>
/// 剪贴板列表查询管线：搜索 → 页签 → 标签；纯计算、无 I/O，便于单元测试。
/// </summary>
public static class ClipboardQuery
{
    /// <summary>
    /// 按页签、搜索词与标签组装视图。<paramref name="tag"/> 为 null 或空表示不按标签筛选。
    /// </summary>
    public static ClipboardListView Build(
        IEnumerable<ClipboardItem> source,
        ClipboardFilter filter,
        string? search = null,
        string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var all = source.ToList();
        var counts = ClipboardCounts.From(all.Where(item => ClipboardSearch.Matches(item, search)));

        var normalizedTag = ClipboardTagPolicy.Normalize(tag);
        var items = new List<ClipboardItem>();

        foreach (var item in all)
        {
            if (!ClipboardFilters.Matches(item, filter))
            {
                continue;
            }

            if (!ClipboardSearch.Matches(item, search))
            {
                continue;
            }

            if (normalizedTag.Length > 0 && !HasTag(item, normalizedTag))
            {
                continue;
            }

            items.Add(item);
        }

        return new ClipboardListView
        {
            Items = items,
            Counts = counts,
            Tags = CollectTags(all),
            TotalCount = all.Count,
            PinnedCount = all.Count(item => item.Pinned),
        };
    }

    /// <summary>标签是否存在于条目上（大小写不敏感）。</summary>
    public static bool HasTag(ClipboardItem item, string tag)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var candidate in item.Tags)
        {
            if (string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>收集去重标签：按出现次数降序，同次数按不区分大小写的字典序。</summary>
    public static IReadOnlyList<string> CollectTags(IEnumerable<ClipboardItem> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var counts = new Dictionary<string, (string Label, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            foreach (var tag in item.Tags)
            {
                var normalized = ClipboardTagPolicy.Normalize(tag);
                if (normalized.Length == 0)
                {
                    continue;
                }

                counts[normalized] = counts.TryGetValue(normalized, out var existing)
                    ? (existing.Label, existing.Count + 1)
                    : (normalized, 1);
            }
        }

        return counts.Values
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Label)
            .ToArray();
    }
}
