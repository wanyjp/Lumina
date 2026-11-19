namespace Lumina.Services.Calendar;

/// <summary>
/// 中国大陆法定节假日与调休上班日。
/// <para>
/// 数据来源（国务院办公厅通知，逐年在 11 月前后发布）：
/// 2026 年《关于 2026 年部分节假日安排的通知》国办发明电〔2025〕7 号（2025-11-04）；
/// 2027 年《关于 2027 年部分节假日安排的通知》国办发明电〔2026〕10 号（2026-11-05）。
/// </para>
/// 未收录的年份按周一至周五判定法定工作日，法定节假日返回 false；
/// 新增年份时补齐 <see cref="Years"/> 与 <see cref="YearSources"/> 即可，调用方无需改动。
/// </summary>
public sealed class CnHolidayCalendar
{
    /// <summary>目前收录的最早年份，供界面提示使用。</summary>
    public const int FirstSupportedYear = 2026;

    private sealed record YearData(HashSet<DateOnly> Holidays, HashSet<DateOnly> MakeupWorkdays);

    private static readonly IReadOnlyDictionary<int, string> YearSources = new Dictionary<int, string>
    {
        [2026] = "国办发明电〔2025〕7 号",
        [2027] = "国办发明电〔2026〕10 号",
    };

    private static readonly IReadOnlyDictionary<int, YearData> Years = new Dictionary<int, YearData>
    {
        [2026] = new(
            Expand(
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)),
                (new DateOnly(2026, 2, 15), new DateOnly(2026, 2, 23)),
                (new DateOnly(2026, 4, 4), new DateOnly(2026, 4, 6)),
                (new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5)),
                (new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 21)),
                (new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 27)),
                (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7))),
            [
                new DateOnly(2026, 1, 4),
                new DateOnly(2026, 2, 14),
                new DateOnly(2026, 2, 28),
                new DateOnly(2026, 5, 9),
                new DateOnly(2026, 9, 20),
                new DateOnly(2026, 10, 10),
            ]),
        [2027] = new(
            Expand(
                (new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 3)),
                (new DateOnly(2027, 2, 5), new DateOnly(2027, 2, 13)),
                (new DateOnly(2027, 4, 3), new DateOnly(2027, 4, 5)),
                (new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 5)),
                (new DateOnly(2027, 6, 9), new DateOnly(2027, 6, 9)),
                (new DateOnly(2027, 9, 15), new DateOnly(2027, 9, 15)),
                (new DateOnly(2027, 10, 1), new DateOnly(2027, 10, 7))),
            [
                new DateOnly(2027, 1, 31),
                new DateOnly(2027, 2, 14),
                new DateOnly(2027, 5, 8),
                new DateOnly(2027, 9, 26),
                new DateOnly(2027, 10, 9),
            ]),
    };

    public static CnHolidayCalendar Default { get; } = new();

    /// <summary>已收录的年份，升序；界面据此提示“当前年份是否有官方数据”。</summary>
    public static IReadOnlyList<int> SupportedYears { get; } = Years.Keys.Order().ToArray();

    public static int LastSupportedYear => SupportedYears[^1];

    /// <summary>全部年份的数据来源，用于设置页与月历脚注。</summary>
    public static string DescribeSources() => string.Join(
        "、",
        SupportedYears.Select(year => $"{YearSources[year]}（{year} 年）"));

    public static string SourceFor(int year) =>
        YearSources.TryGetValue(year, out var source) ? source : string.Empty;

    public bool HasData(int year) => Years.ContainsKey(year);

    public bool IsLegalHoliday(DateOnly date) =>
        Years.TryGetValue(date.Year, out var data) && data.Holidays.Contains(date);

    public bool IsMakeupWorkday(DateOnly date) =>
        Years.TryGetValue(date.Year, out var data) && data.MakeupWorkdays.Contains(date);

    /// <summary>
    /// 法定工作日：调休上班日优先；节假日为 false；其余按周一至周五。
    /// 未收录的年份自然降级为周一至周五。
    /// </summary>
    public bool IsWorkingDay(DateOnly date)
    {
        if (IsMakeupWorkday(date))
        {
            return true;
        }

        if (IsLegalHoliday(date))
        {
            return false;
        }

        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    private static HashSet<DateOnly> Expand(params (DateOnly Start, DateOnly End)[] ranges)
    {
        var days = new HashSet<DateOnly>();

        foreach (var (start, end) in ranges)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                days.Add(date);
            }
        }

        return days;
    }
}
