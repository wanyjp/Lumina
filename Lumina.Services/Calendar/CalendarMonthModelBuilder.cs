using Lumina.Services.Contracts;

namespace Lumina.Services.Calendar;

/// <summary>
/// 月历网格模型构建：固定 42 格（6 周），含前后月补位、农历/节气、今天标记与跨天条带占位。
/// 纯函数，不依赖 WPF 与数据库，便于单元测试。
/// </summary>
public static class CalendarMonthModelBuilder
{
    public const int CellCount = 42;

    public static CalendarMonthModel Build(
        int year,
        int month,
        bool mondayFirst,
        DateOnly? today = null,
        Func<DateOnly, IReadOnlyList<CalendarBarModel>>? barsProvider = null)
    {
        if (year is < 1901 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "月历支持 1901–2100 年。");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "月份应在 1–12 之间。");
        }

        var currentDay = today ?? DateOnly.FromDateTime(DateTime.Now);
        var first = new DateOnly(year, month, 1);
        var offset = mondayFirst
            ? ((int)first.DayOfWeek + 6) % 7
            : (int)first.DayOfWeek;
        var start = first.AddDays(-offset);
        var days = new CalendarDayModel[CellCount];

        for (var index = 0; index < CellCount; index++)
        {
            var date = start.AddDays(index);
            days[index] = new CalendarDayModel(
                date,
                LunarText(date),
                date.Year == year && date.Month == month,
                date == currentDay,
                0,
                barsProvider?.Invoke(date) ?? Array.Empty<CalendarBarModel>());
        }

        return new CalendarMonthModel(year, month, days);
    }

    /// <summary>节气优先替换农历日；超出农历支持范围时返回 null。</summary>
    private static string? LunarText(DateOnly date) =>
        SolarTermCalculator.TermOn(date) ?? LunarCalendarText.DayText(date);
}
