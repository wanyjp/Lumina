using System.Globalization;
using System.Text;

namespace Lumina.Services.Clipboard;

/// <summary>清洗后的 HTML 块类型；只描述排版语义，不携带任何样式或脚本。</summary>
public enum HtmlBlockKind
{
    Paragraph = 0,
    Heading = 1,
    ListItem = 2,
    Quote = 3,
    Preformatted = 4,
}

/// <summary>清洗后的行内片段：只保留加粗/斜体/下划线/等宽与安全链接。</summary>
public sealed record HtmlRun
{
    public required string Text { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Code { get; init; }

    /// <summary>仅保留 http/https/mailto 链接；不用于加载任何资源。</summary>
    public string? Href { get; init; }
}

/// <summary>清洗后的块级内容。</summary>
public sealed record HtmlBlock
{
    public required HtmlBlockKind Kind { get; init; }

    public required IReadOnlyList<HtmlRun> Runs { get; init; }

    /// <summary>标题级别 1–3 或列表缩进层级；其余为 0。</summary>
    public int Level { get; init; }
}

/// <summary>清洗结果；<see cref="Truncated"/> 表示输入超长被截断。</summary>
public sealed record HtmlDocumentModel
{
    public static HtmlDocumentModel Empty { get; } = new()
    {
        Blocks = Array.Empty<HtmlBlock>(),
        Truncated = false,
    };

    public required IReadOnlyList<HtmlBlock> Blocks { get; init; }

    public required bool Truncated { get; init; }

    public bool IsEmpty => Blocks.Count == 0;

    /// <summary>转纯文本：块之间换行，列表项加「· 」前缀，标题与正文同等对待。</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        foreach (var block in Blocks)
        {
            var text = string.Concat(block.Runs.Select(run => run.Text));
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            if (block.Kind == HtmlBlockKind.ListItem)
            {
                builder.Append("· ");
            }

            builder.Append(text);
        }

        return builder.ToString();
    }
}

/// <summary>
/// 剪贴板 HTML 白名单清洗：只解析受限标签集，剥离脚本、样式、事件属性、图片与
/// 一切外部资源；输出与 WPF 无关的中性模型，由 UI 层转成富文本。
/// </summary>
public static class ClipboardHtmlSanitizer
{
    /// <summary>参与解析的最大字符数；超出部分直接截断，避免超长片段拖慢界面。</summary>
    public const int MaxInputLength = 200_000;

    /// <summary>最多保留的块数。</summary>
    public const int MaxBlocks = 400;

    /// <summary>单块最多保留的行内片段数。</summary>
    private const int MaxRunsPerBlock = 200;

    private static readonly HashSet<string> DropWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "noscript", "iframe", "frame", "frameset",
        "object", "embed", "applet", "svg", "math", "template", "canvas", "audio", "video",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav",
        "blockquote", "pre", "ul", "ol", "table", "tbody", "thead", "tfoot", "tr", "td", "th",
        "figure", "figcaption", "dl", "dt", "dd", "address", "center", "form", "fieldset",
    };

    private static readonly HashSet<string> UnderlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "u", "ins",
    };

    private static readonly HashSet<string> CodeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "tt", "kbd", "samp",
    };

    /// <summary>从原始 CF_HTML（可能带 Version/StartFragment 头）中取出片段并清洗。</summary>
    public static HtmlDocumentModel Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return HtmlDocumentModel.Empty;
        }

        var truncated = false;
        var html = ExtractFragment(raw);
        if (html.Length > MaxInputLength)
        {
            html = html[..MaxInputLength];
            truncated = true;
        }

        var (blocks, blocksTruncated) = Parse(html);
        return new HtmlDocumentModel
        {
            Blocks = blocks,
            Truncated = truncated || blocksTruncated,
        };
    }

    /// <summary>把任意 HTML 片段转换为纯文本（供「复制为纯文本」与搜索预览使用）。</summary>
    public static string ToPlainText(string? raw) => Sanitize(raw).ToPlainText();

    /// <summary>
    /// 剥离 CF_HTML 头并取出 StartFragment/EndFragment 之间的内容。
    /// 浏览器写入的剪贴板 HTML 带字节偏移头，直接当标签解析会把头部当成正文。
    /// </summary>
    public static string ExtractFragment(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var text = raw.Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (!text.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var startFragment = ReadOffset(text, "StartFragment:");
        var endFragment = ReadOffset(text, "EndFragment:");

        if (startFragment > 0 && endFragment > startFragment)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (endFragment <= bytes.Length)
            {
                return Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment);
            }
        }

        // 头部存在但偏移不可用时，退化为从第一个标签开始，避免把头部文本渲染出来。
        var firstTag = text.IndexOf('<', StringComparison.Ordinal);
        return firstTag > 0 ? text[firstTag..] : text;
    }

    /// <summary>
    /// 生成带完整 CF_HTML 头的字符串，写回系统剪贴板时富文本编辑器才能识别片段范围。
    /// </summary>
    public static string BuildClipboardHtml(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        const string header = "Version:0.9\r\n"
            + "StartHTML:{0}\r\n"
            + "EndHTML:{1}\r\n"
            + "StartFragment:{2}\r\n"
            + "EndFragment:{3}\r\n";

        // 头部里的偏移本身是 10 位定宽数字，先用占位符估算再回填。
        var prefix = "<html><body>\r\n<!--StartFragment-->";
        var suffix = "<!--EndFragment-->\r\n</body></html>";

        var headerLength = string.Format(
            CultureInfo.InvariantCulture,
            header,
            "0000000000",
            "0000000000",
            "0000000000",
            "0000000000").Length;

        var startHtml = headerLength;
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);

        var formatted = string.Format(
            CultureInfo.InvariantCulture,
            header,
            Pad(startHtml),
            Pad(endHtml),
            Pad(startFragment),
            Pad(endFragment));

        return string.Concat(formatted, prefix, fragment, suffix);
    }

    private static string Pad(int value) =>
        value.ToString("D10", CultureInfo.InvariantCulture);

    /// <summary>
    /// 把清洗结果重建成只含白名单标签的 HTML。回贴时写回该结果而非原始片段，
    /// 保证脚本、样式与外部资源不会经由剪贴板再次流出。
    /// </summary>
    public static string ToSafeHtml(HtmlDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var builder = new StringBuilder();
        foreach (var block in model.Blocks)
        {
            var tag = block.Kind switch
            {
                HtmlBlockKind.Heading => "h" + Math.Clamp(block.Level, 1, 3).ToString(CultureInfo.InvariantCulture),
                HtmlBlockKind.ListItem => "li",
                HtmlBlockKind.Quote => "blockquote",
                HtmlBlockKind.Preformatted => "pre",
                _ => "p",
            };

            builder.Append('<').Append(tag).Append('>');

            foreach (var run in block.Runs)
            {
                var text = Escape(run.Text);
                if (run.Code)
                {
                    text = "<code>" + text + "</code>";
                }

                if (run.Underline)
                {
                    text = "<u>" + text + "</u>";
                }

                if (run.Italic)
                {
                    text = "<em>" + text + "</em>";
                }

                if (run.Bold)
                {
                    text = "<strong>" + text + "</strong>";
                }

                if (!string.IsNullOrEmpty(run.Href))
                {
                    text = "<a href=\"" + Escape(run.Href) + "\">" + text + "</a>";
                }

                builder.Append(text);
            }

            builder.Append("</").Append(tag).Append('>');
        }

        return builder.ToString();
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static int ReadOffset(string text, string key)
    {
        var index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return -1;
        }

        var start = index + key.Length;
        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > start
            && int.TryParse(text[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : -1;
    }

    private static (IReadOnlyList<HtmlBlock> Blocks, bool Truncated) Parse(string html)
    {
        var truncated = false;
        var blocks = new List<HtmlBlock>();
        var runs = new List<HtmlRun>();
        var builder = new StringBuilder();

        var bold = 0;
        var italic = 0;
        var underline = 0;
        var code = 0;
        var preformatted = 0;
        var quote = 0;
        var listDepth = 0;
        var pendingKind = HtmlBlockKind.Paragraph;
        var pendingLevel = 0;
        var currentHref = (string?)null;

        var index = 0;
        while (index < html.Length)
        {
            var ch = html[index];

            if (ch == '<')
            {
                if (index + 3 < html.Length && html.AsSpan(index, 4).SequenceEqual("<!--"))
                {
                    var commentEnd = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    index = commentEnd < 0 ? html.Length : commentEnd + 3;
                    continue;
                }

                var tagEnd = html.IndexOf('>', index);
                if (tagEnd < 0)
                {
                    // 未闭合的尖括号按普通文本处理。
                    builder.Append(ch);
                    index++;
                    continue;
                }

                var inner = html[(index + 1)..tagEnd].Trim();
                index = tagEnd + 1;

                if (inner.Length == 0 || inner[0] == '!' || inner[0] == '?')
                {
                    continue;
                }

                var closing = inner[0] == '/';
                if (closing)
                {
                    inner = inner[1..].Trim();
                }

                var selfClosing = inner.EndsWith('/');
                if (selfClosing)
                {
                    inner = inner[..^1].Trim();
                }

                var nameEnd = 0;
                while (nameEnd < inner.Length && !char.IsWhiteSpace(inner[nameEnd]) && inner[nameEnd] != '/')
                {
                    nameEnd++;
                }

                if (nameEnd == 0)
                {
                    continue;
                }

                var name = inner[..nameEnd];
                var attributes = inner[nameEnd..];

                if (!closing && !selfClosing && DropWithContent.Contains(name))
                {
                    index = SkipElement(html, index, name);
                    continue;
                }

                // <br> / <br/> / <hr> 直接断块，保证原文换行在详情里保留。
                if (!closing
                    && (name.Equals("br", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("hr", StringComparison.OrdinalIgnoreCase)))
                {
                    Flush(pendingKind, pendingLevel);
                    pendingKind = HtmlBlockKind.Paragraph;
                    pendingLevel = 0;
                    continue;
                }

                if (!closing && !selfClosing)
                {
                    switch (name.ToLowerInvariant())
                    {
                        case "b":
                        case "strong":
                            FlushRun();
                            bold++;
                            continue;
                        case "i":
                        case "em":
                            FlushRun();
                            italic++;
                            continue;
                        case "a":
                            FlushRun();
                            if (TryReadHref(attributes, out var href))
                            {
                                currentHref = href;
                            }

                            continue;
                        case "img":
                        case "input":
                        case "button":
                        case "select":
                        case "textarea":
                            // 图片与表单控件一律丢弃，不加载任何外部资源。
                            continue;
                    }

                    if (UnderlineTags.Contains(name))
                    {
                        FlushRun();
                        underline++;
                        continue;
                    }

                    if (CodeTags.Contains(name))
                    {
                        FlushRun();
                        code++;
                        continue;
                    }

                    if (name.Equals("pre", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush(pendingKind, pendingLevel);
                        preformatted++;
                        pendingKind = HtmlBlockKind.Preformatted;
                        pendingLevel = 0;
                        continue;
                    }

                    if (name.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush(pendingKind, pendingLevel);
                        quote++;
                        pendingKind = HtmlBlockKind.Quote;
                        pendingLevel = 0;

                        continue;
                    }

                    if (name.Equals("ul", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("ol", StringComparison.OrdinalIgnoreCase))
                    {
                        listDepth++;
                        continue;
                    }

                    if (name.Equals("li", StringComparison.OrdinalIgnoreCase))
                    {
                        Flush(pendingKind, pendingLevel);
                        pendingKind = HtmlBlockKind.ListItem;
                        pendingLevel = Math.Max(1, listDepth);
                        continue;
                    }

                    var headingLevel = HeadingLevel(name);
                    if (headingLevel > 0)
                    {
                        Flush(pendingKind, pendingLevel);
                        pendingKind = HtmlBlockKind.Heading;
                        pendingLevel = headingLevel;
                        continue;
                    }

                    if (BlockTags.Contains(name))
                    {
                        Flush(pendingKind, pendingLevel);
                        pendingKind = preformatted > 0
                            ? HtmlBlockKind.Preformatted
                            : quote > 0 ? HtmlBlockKind.Quote : HtmlBlockKind.Paragraph;
                        pendingLevel = 0;
                    }

                    // 其余未知标签（含 font/span/small 等）只当作透明容器。
                    continue;
                }

                // 闭合标签
                switch (name.ToLowerInvariant())
                {
                    case "b":
                    case "strong":
                        FlushRun();
                        bold = Math.Max(0, bold - 1);
                        continue;
                    case "i":
                    case "em":
                        FlushRun();
                        italic = Math.Max(0, italic - 1);
                        continue;
                    case "a":
                        FlushRun();
                        currentHref = null;
                        continue;
                }

                if (UnderlineTags.Contains(name))
                {
                    FlushRun();
                    underline = Math.Max(0, underline - 1);
                    continue;
                }

                if (CodeTags.Contains(name))
                {
                    FlushRun();
                    code = Math.Max(0, code - 1);
                    continue;
                }

                if (name.Equals("pre", StringComparison.OrdinalIgnoreCase))
                {
                    Flush(pendingKind, pendingLevel);
                    preformatted = Math.Max(0, preformatted - 1);
                    pendingKind = HtmlBlockKind.Paragraph;
                    pendingLevel = 0;
                    continue;
                }

                if (name.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
                {
                    Flush(pendingKind, pendingLevel);
                    quote = Math.Max(0, quote - 1);
                    pendingKind = quote > 0 ? HtmlBlockKind.Quote : HtmlBlockKind.Paragraph;
                    continue;
                }

                if (name.Equals("ul", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ol", StringComparison.OrdinalIgnoreCase))
                {
                    listDepth = Math.Max(0, listDepth - 1);
                    continue;
                }

                if (name.Equals("li", StringComparison.OrdinalIgnoreCase)
                    || HeadingLevel(name) > 0
                    || BlockTags.Contains(name))
                {
                    Flush(pendingKind, pendingLevel);
                    if (HeadingLevel(name) > 0 || name.Equals("li", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingKind = preformatted > 0
                            ? HtmlBlockKind.Preformatted
                            : quote > 0 ? HtmlBlockKind.Quote : HtmlBlockKind.Paragraph;
                        pendingLevel = 0;
                    }
                }

                continue;
            }

            if (ch == '&')
            {
                if (TryReadEntity(html, index, out var decoded, out var consumed))
                {
                    builder.Append(decoded);
                    index += consumed;
                    continue;
                }

                builder.Append(ch);
                index++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                var preserve = preformatted > 0;
                var next = index + 1;
                while (next < html.Length && char.IsWhiteSpace(html[next]))
                {
                    next++;
                }

                if (preserve)
                {
                    builder.Append(html[index..next]);
                }
                else if (builder.Length > 0)
                {
                    if (builder[^1] != ' ')
                    {
                        builder.Append(' ');
                    }
                }
                else if (runs.Count > 0)
                {
                    // 行内样式切换后紧跟空白：保留一个前导空格，避免「粗体正文」粘连。
                    builder.Append(' ');
                }

                index = next;
                continue;
            }

            builder.Append(ch);
            index++;
        }

        Flush(pendingKind, pendingLevel);
        return (blocks, truncated);

        /// <summary>把已累积文本按当前行内样式收成一个片段。</summary>
        void FlushRun()
        {
            var value = builder.ToString();
            builder.Clear();

            if (runs.Count == 0)
            {
                value = value.TrimStart();
            }

            value = value.TrimEnd();
            if (value.Length == 0)
            {
                return;
            }

            if (runs.Count >= MaxRunsPerBlock)
            {
                truncated = true;
                return;
            }

            runs.Add(new HtmlRun
            {
                Text = value,
                Bold = bold > 0,
                Italic = italic > 0,
                Underline = underline > 0,
                Code = code > 0,
                Href = currentHref,
            });
        }

        /// <summary>结束当前块：先收片段，再落一个块。</summary>
        void Flush(HtmlBlockKind kind, int level)
        {
            FlushRun();

            if (runs.Count == 0)
            {
                return;
            }

            if (blocks.Count >= MaxBlocks)
            {
                truncated = true;
                runs.Clear();
                return;
            }

            blocks.Add(new HtmlBlock
            {
                Kind = kind,
                Level = level,
                Runs = runs.ToArray(),
            });
            runs.Clear();
        }
    }

    /// <summary>跳过带内容的危险标签，返回其闭合标签之后的位置。</summary>
    private static int SkipElement(string html, int from, string name)
    {
        var open = "<" + name;
        var close = "</" + name;
        var depth = 1;
        var cursor = from;

        while (cursor < html.Length)
        {
            var nextClose = html.IndexOf(close, cursor, StringComparison.OrdinalIgnoreCase);
            var nextOpen = html.IndexOf(open, cursor, StringComparison.OrdinalIgnoreCase);

            if (nextClose < 0)
            {
                return html.Length;
            }

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                cursor = nextOpen + open.Length;
                continue;
            }

            depth--;
            var end = html.IndexOf('>', nextClose);
            cursor = end < 0 ? html.Length : end + 1;
            if (depth == 0)
            {
                return cursor;
            }
        }

        return html.Length;
    }

    private static int HeadingLevel(string name)
    {
        if (name.Length != 2
            || (name[0] != 'h' && name[0] != 'H')
            || name[1] < '1' || name[1] > '6')
        {
            return 0;
        }

        return name[1] - '0';
    }

    /// <summary>只接受 http/https/mailto；javascript:/data:/file: 一律丢弃。</summary>
    private static bool TryReadHref(string attributes, out string? href)
    {
        href = null;

        // 按属性名边界匹配：data-href / xhref 不能被当成 href。
        var index = -1;
        var searchFrom = 0;
        while (searchFrom < attributes.Length)
        {
            var candidate = attributes.IndexOf("href", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (candidate < 0)
            {
                break;
            }

            var before = candidate - 1;
            var after = candidate + 4;
            var nameStart = before < 0 || char.IsWhiteSpace(attributes[before]);
            var nameEnd = after >= attributes.Length
                || char.IsWhiteSpace(attributes[after])
                || attributes[after] == '=';

            if (nameStart && nameEnd)
            {
                index = candidate;
                break;
            }

            searchFrom = candidate + 4;
        }

        if (index < 0)
        {
            return false;
        }

        var cursor = index + 4;
        while (cursor < attributes.Length && char.IsWhiteSpace(attributes[cursor]))
        {
            cursor++;
        }

        if (cursor >= attributes.Length || attributes[cursor] != '=')
        {
            return false;
        }

        cursor++;
        while (cursor < attributes.Length && char.IsWhiteSpace(attributes[cursor]))
        {
            cursor++;
        }

        if (cursor >= attributes.Length)
        {
            return false;
        }

        var quote = attributes[cursor];
        string value;
        if (quote is '"' or '\'')
        {
            var end = attributes.IndexOf(quote, cursor + 1);
            if (end < 0)
            {
                return false;
            }

            value = attributes[(cursor + 1)..end];
        }
        else
        {
            var end = cursor;
            while (end < attributes.Length && !char.IsWhiteSpace(attributes[end]) && attributes[end] != '>')
            {
                end++;
            }

            value = attributes[cursor..end];
        }

        value = DecodeEntities(value).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != Uri.UriSchemeHttps
            && uri.Scheme != Uri.UriSchemeMailto)
        {
            return false;
        }

        href = value;
        return true;
    }

    private static bool TryReadEntity(string html, int index, out string decoded, out int consumed)
    {
        decoded = string.Empty;
        consumed = 0;

        var end = html.IndexOf(';', index + 1);
        if (end < 0 || end - index > 10)
        {
            return false;
        }

        var body = html[(index + 1)..end];
        if (body.Length == 0)
        {
            return false;
        }

        if (body[0] == '#')
        {
            var isHex = body.Length > 1 && (body[1] == 'x' || body[1] == 'X');
            var digits = isHex ? body[2..] : body[1..];
            if (digits.Length == 0)
            {
                return false;
            }

            var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out var code))
            {
                return false;
            }

            if (code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                return false;
            }

            decoded = char.ConvertFromUtf32(code);
            consumed = end - index + 1;
            return true;
        }

        var named = body switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            "ensp" => " ",
            "emsp" => " ",
            "thinsp" => " ",
            "mdash" => "—",
            "ndash" => "–",
            "hellip" => "…",
            "middot" => "·",
            "copy" => "©",
            "reg" => "®",
            "trade" => "™",
            "laquo" => "«",
            "raquo" => "»",
            _ => null,
        };

        if (named is null)
        {
            return false;
        }

        decoded = named;
        consumed = end - index + 1;
        return true;
    }

    private static string DecodeEntities(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] == '&'
                && TryReadEntity(value, index, out var decoded, out var consumed))
            {
                builder.Append(decoded);
                index += consumed;
                continue;
            }

            builder.Append(value[index]);
            index++;
        }

        return builder.ToString();
    }
}
