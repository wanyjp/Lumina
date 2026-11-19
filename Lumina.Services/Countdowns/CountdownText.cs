using System.Globalization;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Countdowns;

/// <summary>
/// 倒计时文案的单一来源：列表卡片、放大态、桌面组件与统计详情共用同一套口径，
/// 避免各视图对剩余天数、重复规则与分类名称各写一份。
/// </summary>
public static class CountdownText
{
    private static readonly string[] WeekdayLabels =
    [
        "周日", "周一", "周二", "周三", "周四", "周五", "周六",
    ];

    public static string CategoryLabel(CountdownCategory category) => category switch
    {
        CountdownCategory.Work => "工作",
        CountdownCategory.Life => "生活",
        CountdownCategory.Exam => "考试",
        CountdownCategory.Anniversary => "纪念日",
        _ => "其他",
    };

    /// <summary>重复语义：不重复 / 每周二 / 每月 26 号 / 每月月底 / 每年 11 月 26 日。</summary>
    public static string RepeatSemantic(CountdownItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Repeat switch
        {
            CountdownRepeat.Weekly when item.AnchorWeekday is { } weekday && weekday is >= 0 and <= 6 =>
                "每" + WeekdayLabels[weekday],
            CountdownRepeat.Monthly when item.AnchorMonthEnd => "每月月底",
            CountdownRepeat.Monthly when item.AnchorDay is { } day => $"每月 {day} 号",
            CountdownRepeat.Yearly when item.AnchorMonth is { } month && item.AnchorDay is { } day =>
                $"每年 {month} 月 {day} 日",
            _ => "不重复",
        };
    }

    public static string DateText(DateOnly? date) =>
        date is { } value ? $"{value.Year} 年 {value.Month} 月 {value.Day} 日" : "—";

    /// <summary>
    /// 卡片主文案拆成三段：前缀 / 名称 / 后缀。视图只把名称加粗放大，
    /// 其余文字保持正文样式，避免整行都变成标题。
    /// </summary>
    public static (string Prefix, string Name, string Suffix) LabelParts(
        CountdownItem item,
        UrgencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.NextOccurrence is null)
        {
            return ("日期无效", string.Empty, string.Empty);
        }

        if (snapshot.Level == UrgencyLevel.Expired)
        {
            return (string.Empty, item.Name, "已过");
        }

        if (snapshot.DaysRemaining == 0)
        {
            return ("今天就是", item.Name, string.Empty);
        }

        // 名称自带“截止”语义时读作“项目截止剩余”，与原型一致。
        if (item.Name.Contains("截止", StringComparison.Ordinal))
        {
            return (string.Empty, item.Name, "剩余");
        }

        return (item.Repeat == CountdownRepeat.None ? "距离" : "距离下次", item.Name, "还有");
    }

    /// <summary>卡片主文案：距离 X 还有 / 今天就是 X / X 已过 / 日期无效。</summary>
    public static string Label(CountdownItem item, UrgencySnapshot snapshot)
    {
        var parts = LabelParts(item, snapshot);
        return parts.Prefix + parts.Name + parts.Suffix;
    }

    /// <summary>大号数字：已过期也显示绝对值，符号由剩余文案表达。</summary>
    public static string Number(UrgencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.NextOccurrence is null
            ? "—"
            : Math.Abs(snapshot.DaysRemaining).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>剩余文案：还有 N 天 / 就是今天 / 已过去 N 天。</summary>
    public static string Remaining(UrgencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.NextOccurrence is null)
        {
            return "—";
        }

        if (snapshot.Level == UrgencyLevel.Expired)
        {
            return $"已过去 {Math.Abs(snapshot.DaysRemaining)} 天";
        }

        return snapshot.DaysRemaining == 0 ? "就是今天" : $"还有 {snapshot.DaysRemaining} 天";
    }

    /// <summary>重复规则 + 下次发生日；不重复项只显示日期。</summary>
    public static string Semantic(CountdownItem item, UrgencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.NextOccurrence is null)
        {
            return "日期无效，请重新选择目标日期";
        }

        return item.Repeat == CountdownRepeat.None
            ? DateText(snapshot.NextOccurrence)
            : RepeatSemantic(item) + " · 下次 " + DateText(snapshot.NextOccurrence);
    }

    /// <summary>放大态与卡片辅助说明，兼顾文字表达，避免颜色成为唯一信息载体。</summary>
    public static string UrgencyNote(UrgencyLevel level) => level switch
    {
        UrgencyLevel.Warm => "已进入临近阶段，卡片转为暖色。",
        UrgencyLevel.Hot => "十分临近，色彩加深并出现进度提示。",
        UrgencyLevel.Urgent => "即将到来，卡片以呼吸光效提醒。",
        UrgencyLevel.Expired => "目标日期已过，卡片转为中性灰色。",
        _ => "时间尚充裕，保持常规配色。",
    };
}
