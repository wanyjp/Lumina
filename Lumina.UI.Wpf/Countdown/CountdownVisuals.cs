using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.UI.Wpf.Countdown;

/// <summary>
/// 倒计时视觉映射：分类线性图标与五档紧迫度资源键。
/// 分类只使用中性描边，颜色只表达紧迫度（P3-04 口径）。
/// </summary>
public static class CountdownVisuals
{
    private static readonly IReadOnlyDictionary<CountdownCategory, Geometry> CategoryGeometries =
        new Dictionary<CountdownCategory, Geometry>
        {
            [CountdownCategory.Work] = Geometry.Parse("M3,7 H21 V19 H3 Z M8,7 V4 H16 V7"),
            [CountdownCategory.Life] = Geometry.Parse("M3,11 L12,4 L21,11 M5,10 V19 H19 V10"),
            [CountdownCategory.Exam] = Geometry.Parse("M4,5 H11 V19 H4 Z M13,5 H20 V19 H13 Z"),
            [CountdownCategory.Anniversary] = Geometry.Parse(
                "M12,20 L5,13 C2,10 3,5 7,5 C9.5,5 11,6.5 12,8 C13,6.5 14.5,5 17,5 C21,5 22,10 19,13 Z"),
            [CountdownCategory.Other] = Geometry.Parse("M4,12 A8,8 0 1 1 20,12 A8,8 0 1 1 4,12 Z M12,11 V13"),
        };

    public static Geometry CategoryGeometry(CountdownCategory category) =>
        CategoryGeometries.TryGetValue(category, out var geometry)
            ? geometry
            : CategoryGeometries[CountdownCategory.Other];

    /// <summary>
    /// 主文案富文本：前缀/后缀为次要色正文，名称加粗并略大一号，形成“今天就是 **1**”的层级。
    /// </summary>
    public static TextBlock CreateLabel(
        CountdownItem item,
        UrgencySnapshot snapshot,
        double fontSize,
        double nameFontSize)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(snapshot);

        var (prefix, name, suffix) = CountdownText.LabelParts(item, snapshot);
        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondaryBrush");

        if (prefix.Length > 0)
        {
            label.Inlines.Add(new Run(prefix));
        }

        if (name.Length > 0)
        {
            var nameRun = new Run(name)
            {
                FontWeight = FontWeights.Bold,
                FontSize = nameFontSize,
            };
            nameRun.SetResourceReference(TextElement.ForegroundProperty, "Theme.TextBrush");
            label.Inlines.Add(nameRun);
        }

        if (suffix.Length > 0)
        {
            label.Inlines.Add(new Run(suffix));
        }

        return label;
    }

    public static string UrgencyBrushKey(UrgencyLevel level) => level switch
    {
        UrgencyLevel.Warm => "Theme.Urgency.WarmBrush",
        UrgencyLevel.Hot => "Theme.Urgency.HotBrush",
        UrgencyLevel.Urgent => "Theme.Urgency.UrgentBrush",
        UrgencyLevel.Expired => "Theme.Urgency.ExpiredBrush",
        _ => "Theme.Urgency.CalmBrush",
    };

    public static string UrgencyColorKey(UrgencyLevel level) => level switch
    {
        UrgencyLevel.Warm => "Theme.Color.Urgency.Warm",
        UrgencyLevel.Hot => "Theme.Color.Urgency.Hot",
        UrgencyLevel.Urgent => "Theme.Color.Urgency.Urgent",
        UrgencyLevel.Expired => "Theme.Color.Urgency.Expired",
        _ => "Theme.Color.Urgency.Calm",
    };
}
