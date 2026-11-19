using Lumina.Domain.Countdowns;
using Lumina.Domain.Models;

namespace Lumina.Services.Reminders;

/// <summary>
/// 倒计时提醒的日期入口（P1-12）。P3-03 起实际口径统一在
/// <see cref="CountdownCalendar"/>：短月取最后一天、月底恒取最后一天、2/29 平年取 2/28，
/// 不重复项过期后没有下一次。
/// </summary>
public static class CountdownOccurrenceCalculator
{
    public const int SearchMonths = CountdownCalendar.MonthlySearchMonths;

    public const int SearchYears = CountdownCalendar.YearlySearchYears;

    public static DateOnly? Next(CountdownItem item, DateOnly from) =>
        CountdownCalendar.NextOccurrence(item, from);
}
