using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Domain.Clipboard;

/// <summary>
/// 剪贴板列表页签。前五类按内容格式划分，「固定」按 pinned 标记划分，
/// 因此固定条目同时仍会出现在其格式页签中。
/// </summary>
public enum ClipboardFilter
{
    All = 0,
    Text = 1,
    Link = 2,
    Html = 3,
    Image = 4,
    Pinned = 5,
}

/// <summary>页签顺序、文案与稳定键；UI 与测试共用同一份口径。</summary>
public static class ClipboardFilters
{
    /// <summary>页签展示顺序：全部 / 文本 / 链接 / HTML / 图片 / 固定。</summary>
    public static IReadOnlyList<ClipboardFilter> Tabs { get; } = new[]
    {
        ClipboardFilter.All,
        ClipboardFilter.Text,
        ClipboardFilter.Link,
        ClipboardFilter.Html,
        ClipboardFilter.Image,
        ClipboardFilter.Pinned,
    };

    public static string Label(ClipboardFilter filter) => filter switch
    {
        ClipboardFilter.Text => "文本",
        ClipboardFilter.Link => "链接",
        ClipboardFilter.Html => "HTML",
        ClipboardFilter.Image => "图片",
        ClipboardFilter.Pinned => "固定",
        _ => "全部",
    };

    /// <summary>稳定键，用于设置持久化与测试断言，不随界面文案变化。</summary>
    public static string Key(ClipboardFilter filter) => filter switch
    {
        ClipboardFilter.Text => "text",
        ClipboardFilter.Link => "link",
        ClipboardFilter.Html => "html",
        ClipboardFilter.Image => "image",
        ClipboardFilter.Pinned => "pinned",
        _ => "all",
    };

    public static ClipboardFilter Parse(string? key) => key switch
    {
        "text" => ClipboardFilter.Text,
        "link" => ClipboardFilter.Link,
        "html" => ClipboardFilter.Html,
        "image" => ClipboardFilter.Image,
        "pinned" => ClipboardFilter.Pinned,
        _ => ClipboardFilter.All,
    };

    /// <summary>判断条目是否属于页签；固定页签只看 pinned，与格式无关。</summary>
    public static bool Matches(ClipboardItem item, ClipboardFilter filter)
    {
        ArgumentNullException.ThrowIfNull(item);

        return filter switch
        {
            ClipboardFilter.Text => item.Kind == ClipboardKind.Text,
            ClipboardFilter.Link => item.Kind == ClipboardKind.Link,
            ClipboardFilter.Html => item.Kind == ClipboardKind.Html,
            ClipboardFilter.Image => item.Kind == ClipboardKind.Image,
            ClipboardFilter.Pinned => item.Pinned,
            _ => true,
        };
    }
}

/// <summary>各页签的条目数量，供页签徽标展示。</summary>
public sealed record ClipboardCounts
{
    public int All { get; init; }

    public int Text { get; init; }

    public int Link { get; init; }

    public int Html { get; init; }

    public int Image { get; init; }

    public int File { get; init; }

    public int Pinned { get; init; }

    public int For(ClipboardFilter filter) => filter switch
    {
        ClipboardFilter.Text => Text,
        ClipboardFilter.Link => Link,
        ClipboardFilter.Html => Html,
        ClipboardFilter.Image => Image,
        ClipboardFilter.Pinned => Pinned,
        _ => All,
    };

    public static ClipboardCounts From(IEnumerable<ClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var all = 0;
        var text = 0;
        var link = 0;
        var html = 0;
        var image = 0;
        var file = 0;
        var pinned = 0;

        foreach (var item in items)
        {
            all++;
            if (item.Pinned)
            {
                pinned++;
            }

            switch (item.Kind)
            {
                case ClipboardKind.Link:
                    link++;
                    break;
                case ClipboardKind.Html:
                    html++;
                    break;
                case ClipboardKind.Image:
                    image++;
                    break;
                case ClipboardKind.File:
                    file++;
                    break;
                default:
                    text++;
                    break;
            }
        }

        return new ClipboardCounts
        {
            All = all,
            Text = text,
            Link = link,
            Html = html,
            Image = image,
            File = file,
            Pinned = pinned,
        };
    }
}
