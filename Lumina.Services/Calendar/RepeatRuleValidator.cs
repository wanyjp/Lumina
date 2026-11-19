using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Calendar;

/// <summary>
/// 周期规则校验：只校验字段结构与取值，不做日期展开。
/// 错误以 <see cref="ArgumentException"/> 抛出，消息可直接展示给用户。
/// </summary>
public static class RepeatRuleValidator
{
    public const int MaxCount = 366;

    public static void Validate(RepeatRule rule, DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(rule);

        switch (rule.Mode)
        {
            case RepeatMode.Week:
                RequireRange(rule.WeekStart, rule.WeekEnd, "星期区间应为 1–7（1=周一，7=周日）。", 1, 7);
                break;

            case RepeatMode.Month:
                if (rule.MonthStart is not { } monthStart || monthStart is < 1 or > 31)
                {
                    throw new ArgumentException("每月区间的开始日应为 1–31。", nameof(rule));
                }

                if (ParseBound(rule.MonthEnd) is not { } monthEnd)
                {
                    throw new ArgumentException("每月区间的结束日应为 1–31 或 end。", nameof(rule));
                }

                if (monthStart > monthEnd)
                {
                    throw new ArgumentException("每月区间的开始日不能大于结束日。", nameof(rule));
                }

                break;

            case RepeatMode.Weekdays:
                if (rule.Weekdays is not { Count: > 0 } weekdays
                    || weekdays.Any(day => day is < 0 or > 6)
                    || weekdays.Distinct().Count() != weekdays.Count)
                {
                    throw new ArgumentException("星期多选至少选择一天，取值 0–6。", nameof(rule));
                }

                break;

            case RepeatMode.MonthDay:
                if (ParseBound(rule.MonthDay) is not { } monthDay || monthDay is < 1 or > 31)
                {
                    throw new ArgumentException("每月日期应为 1–31 或 end。", nameof(rule));
                }

                break;

            case RepeatMode.Daily:
            case RepeatMode.Workday:
            case RepeatMode.Holiday:
                break;

            default:
                throw new ArgumentException("未知的周期方式。", nameof(rule));
        }

        switch (rule.EndType)
        {
            case RepeatEndType.Date:
                if (rule.EndDate is not { } endDate || endDate < startDate)
                {
                    throw new ArgumentException("周期结束日期不能早于开始日期。", nameof(rule));
                }

                break;

            case RepeatEndType.Count:
                if (rule.Count is not { } count || count is < 1 or > MaxCount)
                {
                    throw new ArgumentException($"周期次数应在 1–{MaxCount} 之间。", nameof(rule));
                }

                break;

            case RepeatEndType.Forever:
                break;

            default:
                throw new ArgumentException("未知的周期结束条件。", nameof(rule));
        }
    }

    /// <summary>解析 1–31 或 `end`；非法返回 null。</summary>
    internal static int? ParseBound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value.Trim(), "end", StringComparison.OrdinalIgnoreCase))
        {
            return 31;
        }

        return int.TryParse(value.Trim(), out var number) && number is >= 1 and <= 31
            ? number
            : null;
    }

    private static void RequireRange(
        int? start,
        int? end,
        string message,
        int minimum,
        int maximum)
    {
        if (start is not { } from || from < minimum || from > maximum
            || end is not { } to || to < minimum || to > maximum)
        {
            throw new ArgumentException(message);
        }
    }
}
