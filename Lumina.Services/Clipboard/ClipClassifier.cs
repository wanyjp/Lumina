using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lumina.Domain.Enums;

namespace Lumina.Services.Clipboard;

/// <summary>
/// 剪贴板格式分类与归一化：纯函数，供监听适配器与测试共用。
/// 分类优先级：图片 → 文件 → HTML → 链接 → 文本。
/// </summary>
public static class ClipClassifier
{
    private static readonly Regex HorizontalWhitespace = new("[ \t]+", RegexOptions.Compiled);

    /// <summary>按可用格式判定条目类型。</summary>
    public static ClipboardKind Classify(
        string? text,
        string? html,
        IReadOnlyList<string>? files,
        bool hasImage)
    {
        if (hasImage)
        {
            return ClipboardKind.Image;
        }

        if (files is { Count: > 0 })
        {
            return ClipboardKind.File;
        }

        if (!string.IsNullOrWhiteSpace(html))
        {
            return ClipboardKind.Html;
        }

        if (IsWebLink(text))
        {
            return ClipboardKind.Link;
        }

        return ClipboardKind.Text;
    }

    /// <summary>文本归一化：统一换行、折叠行内空白、去首尾空白，用于去重哈希。</summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = unified.Split('\n');
        var normalizedLines = new List<string>(lines.Length);
        var previousBlank = false;
        foreach (var line in lines)
        {
            var normalized = HorizontalWhitespace.Replace(line, " ").Trim();
            if (normalized.Length == 0)
            {
                if (normalizedLines.Count == 0 || previousBlank)
                {
                    previousBlank = true;
                    continue;
                }

                previousBlank = true;
                normalizedLines.Add(string.Empty);
                continue;
            }

            previousBlank = false;
            normalizedLines.Add(normalized);
        }

        return string.Join("\n", normalizedLines).Trim();
    }

    /// <summary>判断整段文本是否为单个 http/https 链接。</summary>
    public static bool IsWebLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>对规范化文本计算稳定哈希；类型参与哈希，避免同文本跨类型误合并。</summary>
    public static string ComputeTextHash(ClipboardKind kind, string canonical)
    {
        var payload = KindKey(kind) + "\n" + canonical;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    /// <summary>对二进制内容计算稳定哈希，用于图片去重。</summary>
    public static string ComputeBytesHash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>数据库与日志使用的稳定类型键。</summary>
    public static string KindKey(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Link => "link",
        ClipboardKind.Image => "image",
        ClipboardKind.Html => "html",
        ClipboardKind.File => "file",
        _ => "text",
    };
}
