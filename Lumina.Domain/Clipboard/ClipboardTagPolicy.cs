using System.Text;

namespace Lumina.Domain.Clipboard;

/// <summary>
/// 剪贴板标签口径：最多 5 个、每个最多 12 字；去空白、忽略大小写去重。
/// 常量放在 Domain 以便 Data / Services / UI 共用同一份限制，避免两套口径。
/// </summary>
public static class ClipboardTagPolicy
{
    public const int MaxCount = 5;

    public const int MaxLength = 12;

    /// <summary>标签输入分隔符：逗号（中英文）、顿号、分号与空白。</summary>
    private static readonly char[] Separators = [',', '，', '、', ';', '；', ' ', '\t', '\r', '\n'];

    /// <summary>标准化单个标签：折叠内部空白并截断到 12 字；空白返回空串。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(value);
        return Truncate(collapsed, MaxLength);
    }

    /// <summary>
    /// 标准化一组标签：逐个 trim 与截断、忽略大小写去重、保持输入顺序，最多保留 5 个。
    /// <paramref name="dropped"/> 返回因超出数量或为空被丢弃的个数，供界面提示。
    /// </summary>
    public static IReadOnlyList<string> NormalizeAll(
        IEnumerable<string>? values,
        out int dropped)
    {
        dropped = 0;
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(MaxCount);
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                dropped++;
                continue;
            }

            if (result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                dropped++;
                continue;
            }

            if (result.Count >= MaxCount)
            {
                dropped++;
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    /// <summary>解析自由输入（逗号/顿号/分号/空白分隔）为标签集合。</summary>
    public static IReadOnlyList<string> FromInput(string? input, out int dropped)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            dropped = 0;
            return Array.Empty<string>();
        }

        var parts = input.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return NormalizeAll(parts, out dropped);
    }

    /// <summary>把标签集合序列化为编辑框可读文本。</summary>
    public static string ToInput(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return string.Empty;
        }

        return string.Join("，", tags);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>按字符截断；避免在代理对中间切断产生乱码。</summary>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut];
    }
}
