using System.Text;

namespace Lumina.Services.Notes;

/// <summary>
/// 清单文本处理：把整段文本按行转换成 `☐/☑` 清单、剥离标记、或把
/// `[ ] / [x] / [X] / [√]` 等写法归一到 `☐ / ☑`。
/// 空行原样保留；混合内容按行判断，非清单行在「转为清单」时补 `☐ `。
/// </summary>
public static class ChecklistText
{
    public const string Unchecked = "☐";
    public const string Checked = "☑";

    private static readonly string[] CheckMarkers = ["[x]", "[X]", "[√]", "☑"];
    private static readonly string[] UncheckedMarkers = ["[ ]", "[]", "☐"];

    /// <summary>解析单行是否为清单项，返回缩进、勾选状态与去掉标记后的正文。</summary>
    public static bool TryParseLine(string line, out string indent, out bool isChecked, out string body)
    {
        indent = string.Empty;
        isChecked = false;
        body = line;

        var trimmedStart = line.Length - line.TrimStart().Length;
        var rest = line[trimmedStart..];
        foreach (var marker in CheckMarkers)
        {
            if (rest.StartsWith(marker, StringComparison.Ordinal))
            {
                indent = line[..trimmedStart];
                isChecked = true;
                body = TrimSeparator(rest[marker.Length..]);
                return true;
            }
        }

        foreach (var marker in UncheckedMarkers)
        {
            if (rest.StartsWith(marker, StringComparison.Ordinal))
            {
                indent = line[..trimmedStart];
                body = TrimSeparator(rest[marker.Length..]);
                return true;
            }
        }

        return false;
    }

    /// <summary>每个非空行补 `☐ `；已是清单行则只做标记归一。</summary>
    public static string ToChecklist(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length + 8);
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                builder.Append(line);
                continue;
            }

            if (TryParseLine(line, out var indent, out var isChecked, out var body))
            {
                builder.Append(indent)
                    .Append(isChecked ? Checked : Unchecked)
                    .Append(' ')
                    .Append(body);
                continue;
            }

            builder.Append(Unchecked).Append(' ').Append(line);
        }

        return builder.ToString();
    }

    /// <summary>剥离清单标记，保留缩进与正文；非清单行原样保留。</summary>
    public static string ToPlainText(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length);
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            var line = lines[index];
            if (TryParseLine(line, out var indent, out _, out var body))
            {
                builder.Append(indent).Append(body);
                continue;
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    /// <summary>把各种勾选写法统一成 `☐ / ☑`；不改动非清单行。</summary>
    public static string Normalize(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length);
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            var line = lines[index];
            if (TryParseLine(line, out var indent, out var isChecked, out var body))
            {
                builder.Append(indent)
                    .Append(isChecked ? Checked : Unchecked)
                    .Append(' ')
                    .Append(body);
                continue;
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 切换第 <paramref name="lineIndex"/> 行（0 起）的勾选状态；
    /// 该行不是清单项时返回 false，<paramref name="updated"/> 原样返回。
    /// </summary>
    public static bool TryToggle(string? content, int lineIndex, out string updated)
    {
        updated = content ?? string.Empty;
        if (string.IsNullOrEmpty(content) || lineIndex < 0)
        {
            return false;
        }

        var lines = SplitLines(content);
        if (lineIndex >= lines.Count)
        {
            return false;
        }

        if (!TryParseLine(lines[lineIndex], out var indent, out var isChecked, out var body))
        {
            return false;
        }

        lines[lineIndex] = string.Concat(
            indent,
            isChecked ? Unchecked : Checked,
            " ",
            body);
        updated = string.Join('\n', lines);
        return true;
    }

    /// <summary>统计清单项与已完成数量（用于卡片进度）。</summary>
    public static (int Total, int Done) Count(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return (0, 0);
        }

        var total = 0;
        var done = 0;
        foreach (var line in SplitLines(content))
        {
            if (TryParseLine(line, out _, out var isChecked, out _))
            {
                total++;
                if (isChecked)
                {
                    done++;
                }
            }
        }

        return (total, done);
    }

    private static string TrimSeparator(string value) =>
        value.Length > 0 && value[0] == ' ' ? value[1..] : value;

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
}
