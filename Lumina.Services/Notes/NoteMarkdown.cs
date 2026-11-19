using System.Text;
using System.Text.RegularExpressions;

namespace Lumina.Services.Notes;

/// <summary>预览块类型：白名单只有这五种，其余语法按纯文本处理。</summary>
public enum MarkdownBlockKind
{
    Paragraph,
    Bullet,
    Ordered,
    Check,
    Code,
}

/// <summary>行内片段：纯文本、加粗、行内代码。</summary>
public enum MarkdownInlineKind
{
    Text,
    Bold,
    Code,
}

public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text);

public sealed record MarkdownBlock
{
    public required MarkdownBlockKind Kind { get; init; }

    public IReadOnlyList<MarkdownInline> Inlines { get; init; } = [];

    /// <summary>清单项是否勾选（仅 <see cref="MarkdownBlockKind.Check"/>）。</summary>
    public bool Checked { get; init; }

    /// <summary>有序列表序号（仅 <see cref="MarkdownBlockKind.Ordered"/>）。</summary>
    public int Number { get; init; }
}

/// <summary>
/// 便签 Markdown 预览解析：只认「段落 / 无序列表 / 有序列表 / 清单 / 代码块」，
/// 行内只认 `**加粗**` 与 `` `代码` ``。原始 HTML 一律当普通文字，
/// 不会被任何渲染层解释（WPF 侧全部用 TextBlock/Run 输出，天然转义）。
/// </summary>
public static partial class NoteMarkdown
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var blocks = new List<MarkdownBlock>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    FlushCode(blocks, code);
                    inCode = false;
                }
                else
                {
                    FlushParagraph(blocks, paragraph);
                    inCode = true;
                    code.Clear();
                }

                continue;
            }

            if (inCode)
            {
                code.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(blocks, paragraph);
                continue;
            }

            if (ChecklistText.TryParseLine(line, out _, out var isChecked, out var checkBody))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Check,
                    Checked = isChecked,
                    Inlines = ParseInlines(checkBody),
                });
                continue;
            }

            if (TryBullet(trimmed, out var bulletBody))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Bullet,
                    Inlines = ParseInlines(bulletBody),
                });
                continue;
            }

            var ordered = OrderedPattern().Match(trimmed);
            if (ordered.Success)
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Ordered,
                    Number = int.Parse(ordered.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    Inlines = ParseInlines(trimmed[ordered.Length..]),
                });
                continue;
            }

            paragraph.Add(line);
        }

        if (inCode)
        {
            FlushCode(blocks, code);
        }

        FlushParagraph(blocks, paragraph);
        return blocks;
    }

    /// <summary>行内加粗与行内代码；未闭合的标记按普通字符输出。</summary>
    public static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var inlines = new List<MarkdownInline>();
        var buffer = new StringBuilder();
        var index = 0;

        void Flush()
        {
            if (buffer.Length > 0)
            {
                inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, buffer.ToString()));
                buffer.Clear();
            }
        }

        while (index < text.Length)
        {
            if (text[index] == '*'
                && index + 1 < text.Length
                && text[index + 1] == '*')
            {
                var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
                if (end > index + 2)
                {
                    Flush();
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Bold, text[(index + 2)..end]));
                    index = end + 2;
                    continue;
                }
            }
            else if (text[index] == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end > index + 1)
                {
                    Flush();
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Code, text[(index + 1)..end]));
                    index = end + 1;
                    continue;
                }
            }

            buffer.Append(text[index]);
            index++;
        }

        Flush();
        return inlines;
    }

    private static bool TryBullet(string trimmed, out string body)
    {
        foreach (var prefix in BulletPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                body = trimmed[prefix.Length..];
                return true;
            }
        }

        body = string.Empty;
        return false;
    }

    private static void FlushParagraph(List<MarkdownBlock> blocks, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        blocks.Add(new MarkdownBlock
        {
            Kind = MarkdownBlockKind.Paragraph,
            Inlines = ParseInlines(string.Join('\n', lines)),
        });
        lines.Clear();
    }

    private static void FlushCode(List<MarkdownBlock> blocks, List<string> lines)
    {
        blocks.Add(new MarkdownBlock
        {
            Kind = MarkdownBlockKind.Code,
            Inlines = [new MarkdownInline(MarkdownInlineKind.Text, string.Join('\n', lines))],
        });
        lines.Clear();
    }

    [GeneratedRegex(@"^(\d{1,3})(?:[.)]\s+|、\s*)", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedPattern();

    private static readonly string[] BulletPrefixes = ["- ", "* ", "• "];
}
