using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Calendar;

/// <summary>
/// 周期规则引擎：按自然日逐日判断，支持 7 种周期方式与 3 种结束条件。
/// 单系列最多扫描 3700 天；日期键统一使用 <see cref="DateOnly"/>，不经过字符串。
/// </summary>
public static class RepeatRuleEngine
{
    public const int MaxDaysPerSeries = 3700;
    public const int MaxPreviewCount = 5;

    /// <summary>
    /// 返回规则在 [rangeStart, rangeEnd] 内的命中日期。
    /// Count 条件从系列开始日累计；Date 条件含结束当天；Forever 受 3700 天上限约束。
    /// </summary>
    public static IReadOnlyList<DateOnly> Occurrences(
        RepeatRule rule,
        DateOnly seriesStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rangeEnd < rangeStart)
        {
            return Array.Empty<DateOnly>();
        }

        var occurrences = new List<DateOnly>();
        var scanEnd = Min(seriesStart.AddDays(MaxDaysPerSeries - 1), rangeEnd);
        var endDate = rule.EndType == RepeatEndType.Date ? rule.EndDate : null;
        var countTarget = rule.EndType == RepeatEndType.Count
            ? Math.Max(1, rule.Count ?? MaxPreviewCount)
            : 0;
        var matched = 0;

        for (var date = seriesStart; date <= scanEnd; date = date.AddDays(1))
        {
            if (endDate is { } end && date > end)
            {
                break;
            }

            if (!Matches(rule, date))
            {
                continue;
            }

            matched++;

            if (date >= rangeStart)
            {
                occurrences.Add(date);
            }

            if (rule.EndType == RepeatEndType.Count && matched >= countTarget)
            {
                break;
            }
        }

        return occurrences;
    }

    /// <summary>从系列开始日返回前 N 个命中日期，供表单预览。</summary>
    public static IReadOnlyList<DateOnly> Preview(
        RepeatRule rule,
        DateOnly seriesStart,
        int take = MaxPreviewCount)
    {
        if (take <= 0)
        {
            return Array.Empty<DateOnly>();
        }

        var rangeEnd = seriesStart.AddDays(MaxDaysPerSeries - 1);
        return Occurrences(rule, seriesStart, seriesStart, rangeEnd)
            .Take(take)
            .ToArray();
    }

    public static bool Matches(RepeatRule rule, DateOnly date)
    {
        return rule.Mode switch
        {
            RepeatMode.Week => MatchesWeekRange(rule, date),
            RepeatMode.Month => MatchesMonthRange(rule, date),
            RepeatMode.Weekdays => rule.Weekdays is { } days && days.Contains(WeekdayIndex(date)),
            RepeatMode.MonthDay => MatchesMonthDay(rule, date),
            RepeatMode.Daily => true,
            RepeatMode.Workday => CnHolidayCalendar.Default.IsWorkingDay(date),
            RepeatMode.Holiday => MatchesHoliday(date),
            _ => false,
        };
    }

    /// <summary>
    /// 把母记录展开为查询区间内的实例；实例保留母记录 Id 与时间字段，
    /// 跨天时长按母记录平移，便于 P1-08 时间轴展示。
    /// </summary>
    public static IReadOnlyList<ScheduleEvent> Expand(
        ScheduleEvent master,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        ArgumentNullException.ThrowIfNull(master);

        if (master.Repeat is null)
        {
            return [master];
        }

        var durationDays = (master.EndDate ?? master.StartDate).DayNumber
            - master.StartDate.DayNumber;
        var dates = Occurrences(master.Repeat, master.StartDate, rangeStart, rangeEnd);
        var instances = new List<ScheduleEvent>(dates.Count);

        foreach (var date in dates)
        {
            instances.Add(master with
            {
                StartDate = date,
                EndDate = durationDays > 0 ? date.AddDays(durationDays) : null,
            });
        }

        return instances;
    }

    private static bool MatchesWeekRange(RepeatRule rule, DateOnly date)
    {
        if (rule.WeekStart is not { } start || rule.WeekEnd is not { } end)
        {
            return false;
        }

        var day = IsoWeekday(date);

        return start <= end
            ? day >= start && day <= end
            : day >= start || day <= end;
    }

    private static bool MatchesMonthRange(RepeatRule rule, DateOnly date)
    {
        if (rule.MonthStart is not { } start)
        {
            return false;
        }

        var end = IsMonthEnd(rule.MonthEnd)
            ? DateTime.DaysInMonth(date.Year, date.Month)
            : RepeatRuleValidator.ParseBound(rule.MonthEnd);

        return end is { } finish && date.Day >= start && date.Day <= finish;
    }

    private static bool MatchesMonthDay(RepeatRule rule, DateOnly date)
    {
        if (IsMonthEnd(rule.MonthDay))
        {
            return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
        }

        return RepeatRuleValidator.ParseBound(rule.MonthDay) == date.Day;
    }

    private static bool MatchesHoliday(DateOnly date)
    {
        var calendar = CnHolidayCalendar.Default;

        // 未来年份无法定节假日数据时，按普通周末降级。
        return calendar.HasData(date.Year)
            ? calendar.IsLegalHoliday(date)
            : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private static bool IsMonthEnd(string? value) =>
        value is not null && string.Equals(value.Trim(), "end", StringComparison.OrdinalIgnoreCase);

    /// <summary>1=周一 … 7=周日。</summary>
    private static int IsoWeekday(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    /// <summary>0=周日 … 6=周六。</summary>
    private static int WeekdayIndex(DateOnly date) => (int)date.DayOfWeek;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
