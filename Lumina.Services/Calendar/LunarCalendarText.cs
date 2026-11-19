using System.Globalization;

namespace Lumina.Services.Calendar;

/// <summary>
/// 农历短文本：初一显示月名，其余显示日名，不带“农历”前缀，供月历小字使用。
/// 使用系统 <see cref="ChineseLunisolarCalendar"/>（1901–2100），超出范围返回 null。
/// </summary>
public static class LunarCalendarText
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] MonthNames =
    [
        "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊",
    ];

    private static readonly string[] DayNames =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十",
    ];

    public static string? DayText(DateOnly date)
    {
        try
        {
            var dateTime = date.ToDateTime(TimeOnly.MinValue);
            var lunarYear = Calendar.GetYear(dateTime);
            var lunarMonth = Calendar.GetMonth(dateTime);
            var lunarDay = Calendar.GetDayOfMonth(dateTime);
            var leapMonth = Calendar.GetLeapMonth(lunarYear);
            var isLeap = leapMonth > 0 && lunarMonth == leapMonth;
            var monthIndex = Math.Clamp(isLeap ? lunarMonth - 2 : lunarMonth - 1, 0, MonthNames.Length - 1);

            if (lunarDay == 1)
            {
                return $"{(isLeap ? "闰" : string.Empty)}{MonthNames[monthIndex]}月";
            }

            return DayNames[Math.Clamp(lunarDay - 1, 0, DayNames.Length - 1)];
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
