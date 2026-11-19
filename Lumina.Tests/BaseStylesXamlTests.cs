using System.Xml.Linq;

namespace Lumina.Tests;

/// <summary>
/// P2-04 启动崩溃回归守卫：WPF 资源字典的隐式 Style 键在运行时
/// （App.InitializeComponent → ResourceDictionary.SetKeys）才解析，
/// 标记编译与单元测试都不会报错。同一字典出现两个同类型隐式键会直接
/// 抛 XamlParseException 导致应用无法启动，且编译/CI 全绿无法发现。
/// 这里用静态 XML 检查把该约束前移到自动回归。
/// </summary>
public class BaseStylesXamlTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void Implicit_style_keys_are_unique()
    {
        var document = XDocument.Load(FindBaseStyles());

        var implicitKeys = document
            .Descendants(PresentationNamespace + "Style")
            .Where(style => style.Attribute(XamlNamespace + "Key") is null)
            .Select(style => NormalizeTargetType(style.Attribute("TargetType")?.Value))
            .ToList();

        var duplicates = implicitKeys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ×{group.Count()}")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"BaseStyles.xaml 存在重复的隐式 Style 键：{string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Keyed_style_keys_are_unique()
    {
        var document = XDocument.Load(FindBaseStyles());

        // 动态键（{x:Static ...}）由运行时求值，不参与字符串唯一性检查。
        var keys = document
            .Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key) && !key!.StartsWith('{'))
            .Select(key => key!)
            .ToList();

        var duplicates = keys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ×{group.Count()}")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"BaseStyles.xaml 存在重复的显式资源键：{string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Calendar_styles_are_reachable()
    {
        var document = XDocument.Load(FindBaseStyles());
        var keys = document
            .Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in new[]
                 {
                     "Theme.Calendar",
                     "Theme.CalendarDayButton",
                     "Theme.CalendarButton",
                     "Theme.CalendarItem",
                 })
        {
            Assert.Contains(name, keys);
        }
    }

    /// <summary>把 {x:Type primitives:CalendarDayButton} 与 {x:Type CalendarDayButton} 归一为同一类型名。</summary>
    private static string NormalizeTargetType(string? targetType)
    {
        var value = (targetType ?? string.Empty).Trim();

        if (value.StartsWith("{x:Type ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            value = value[8..^1].Trim();
        }
        else if (value.StartsWith('{') && value.EndsWith('}'))
        {
            value = value[1..^1].Trim();
        }

        var separator = value.LastIndexOf(':');
        if (separator >= 0)
        {
            value = value[(separator + 1)..];
        }

        return value;
    }

    private static string FindBaseStyles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "Lumina.UI.Wpf",
                "Themes",
                "BaseStyles.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "未找到 Lumina.UI.Wpf/Themes/BaseStyles.xaml，无法执行资源字典守卫。");
    }
}
