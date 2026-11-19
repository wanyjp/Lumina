using Lumina.Domain.Models;

namespace Lumina.Domain.Clipboard;

/// <summary>
/// 剪贴板实时搜索：大小写不敏感的子串匹配，覆盖正文、备注、标签与来源，
/// 文件类条目额外匹配路径列表，链接类匹配 URL。
/// </summary>
public static class ClipboardSearch
{
    /// <summary>查询长度上限，避免超长粘贴把匹配退化为全表扫描。</summary>
    public const int MaxQueryLength = 100;

    /// <summary>归一化查询：去首尾空白并截断到上限；空串表示不过滤。</summary>
    public static string Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.Trim();
        return trimmed.Length > MaxQueryLength ? trimmed[..MaxQueryLength] : trimmed;
    }

    public static bool IsActive(string? query) => Normalize(query).Length > 0;

    /// <summary>条目是否命中查询；空查询一律命中。</summary>
    public static bool Matches(ClipboardItem item, string? query)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (Contains(item.TextContent, normalized)
            || Contains(item.NormalizedText, normalized)
            || Contains(item.Note, normalized)
            || Contains(item.SourceWindow, normalized)
            || Contains(item.Url, normalized)
            || Contains(item.FilePath, normalized))
        {
            return true;
        }

        foreach (var tag in item.Tags)
        {
            if (Contains(tag, normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrEmpty(source)
        && source.Contains(query, StringComparison.OrdinalIgnoreCase);
}
