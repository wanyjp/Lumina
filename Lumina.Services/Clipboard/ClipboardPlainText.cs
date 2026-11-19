using System.Text.Json;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Clipboard;

/// <summary>剪贴板条目的纯文本表示：用于「复制为纯文本」、搜索预览与文件列表展示。</summary>
public static class ClipboardPlainText
{
    /// <summary>
    /// 取条目的纯文本形式；图片没有可复制的文本，返回 null。
    /// HTML 走白名单清洗后取正文，避免把标签当文本复制出去。
    /// </summary>
    public static string? Of(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind switch
        {
            ClipboardKind.Image => null,
            ClipboardKind.Html => HtmlToText(item.TextContent),
            ClipboardKind.File => string.Join(Environment.NewLine, FilePaths(item)),
            _ => item.TextContent,
        };
    }

    /// <summary>解析文件类条目的路径数组；损坏值返回空集合。</summary>
    public static IReadOnlyList<string> FilePaths(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.FilePath))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(item.FilePath) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>取路径的文件名部分；目录或非法路径回落原文。</summary>
    public static string FileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>列表预览文本：折叠换行与连续空白，并截断到指定长度。</summary>
    public static string Preview(ClipboardItem item, int maxLength = 160)
    {
        ArgumentNullException.ThrowIfNull(item);

        var raw = item.Kind switch
        {
            ClipboardKind.Image => item.Width is { } width && item.Height is { } height
                ? $"图片 {width} × {height}"
                : "图片",
            ClipboardKind.File => string.Join(
                "、",
                FilePaths(item).Select(FileName).Where(name => name.Length > 0)),
            ClipboardKind.Html => HtmlToText(item.TextContent),
            _ => item.TextContent ?? string.Empty,
        };

        var flattened = CollapseWhitespace(raw);
        return flattened.Length > maxLength ? flattened[..maxLength] + "…" : flattened;
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // 清洗后没有正文就返回空串：绝不把原始 HTML（可能含 script/属性）当纯文本回填。
        return ClipboardHtmlSanitizer.ToPlainText(html);
    }

    private static string CollapseWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var ch in value)
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
}
