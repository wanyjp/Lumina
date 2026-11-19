using System.Globalization;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clock;

/// <summary>时钟显示文案；业务层只提供 UTC/本地时间，格式化集中在此，便于单测。</summary>
public sealed record ClockDisplay(
    string TimeText,
    string? PeriodText,
    string DateText,
    string? LunarText);

/// <summary>12/24 制、现代时段/十二时辰与日期/农历格式化。</summary>
public static class ClockTextFormatter
{
    public const string ModernPeriodMode = "modern";
    public const string AncientPeriodMode = "ancient";
    public const string NoPeriodMode = "none";

    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];
    private static readonly string[] Shichen = ["子时", "丑时", "寅时", "卯时", "辰时", "巳时", "午时", "未时", "申时", "酉时", "戌时", "亥时"];
    private static readonly string[] MonthNames = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
    private static readonly string[] DayNames =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十",
    ];

    public static ClockDisplay Format(ClockSnapshot snapshot, bool use12Hour, string? periodMode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ClockDisplay(
            FormatTime(snapshot.Local, use12Hour),
            FormatPeriod(snapshot.Local, use12Hour, periodMode),
            FormatDate(snapshot.Local),
            snapshot.LunarText);
    }

    public static string FormatTime(DateTimeOffset local, bool use12Hour) =>
        local.ToString(use12Hour ? "hh:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);

    public static string? FormatPeriod(DateTimeOffset local, bool use12Hour, string? periodMode)
    {
        // 时段/时辰不再随 12 小时制隐藏；24 小时制同样显示，用户可选择「不显示」。
        var mode = NormalizePeriodMode(periodMode);
        if (mode == NoPeriodMode)
        {
            return null;
        }

        return mode == AncientPeriodMode
            ? ShichenText(local)
            : ModernPeriodText(local.Hour);
    }

    public static string FormatDate(DateTimeOffset local) =>
        $"{local.Year} 年 {local.Month} 月 {local.Day} 日 星期{Weekdays[(int)local.DayOfWeek]}";

    public static string FormatShortDate(DateTimeOffset local, string? lunarText)
    {
        var text = $"{local.Month}月{local.Day}日 · 周{Weekdays[(int)local.DayOfWeek]}";
        return string.IsNullOrWhiteSpace(lunarText) ? text : $"{text} · {lunarText}";
    }

    public static string NormalizePeriodMode(string? mode)
    {
        var normalized = mode?.Trim();
        if (string.Equals(normalized, AncientPeriodMode, StringComparison.OrdinalIgnoreCase))
        {
            return AncientPeriodMode;
        }

        return string.Equals(normalized, NoPeriodMode, StringComparison.OrdinalIgnoreCase)
            ? NoPeriodMode
            : ModernPeriodMode;
    }

    public static string ModernPeriodText(int hour)
    {
        if (hour is >= 5 and < 8)
        {
            return "清晨";
        }

        if (hour is >= 8 and < 12)
        {
            return "上午";
        }

        if (hour is >= 12 and < 14)
        {
            return "中午";
        }

        if (hour is >= 14 and < 18)
        {
            return "下午";
        }

        return "晚上";
    }

    public static string ShichenText(DateTimeOffset local)
    {
        var index = (int)Math.Floor((local.Hour + (local.Minute / 60.0) + 1) / 2) % 12;
        return Shichen[index];
    }

    public static string? LunarText(DateOnly date)
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var dateTime = date.ToDateTime(TimeOnly.MinValue);
            var lunarYear = calendar.GetYear(dateTime);
            var lunarMonth = calendar.GetMonth(dateTime);
            var lunarDay = calendar.GetDayOfMonth(dateTime);
            var leapMonth = calendar.GetLeapMonth(lunarYear);
            var isLeap = leapMonth > 0 && lunarMonth == leapMonth;
            var monthIndex = Math.Clamp(isLeap ? lunarMonth - 2 : lunarMonth - 1, 0, MonthNames.Length - 1);
            var monthName = MonthNames[monthIndex];

            if (lunarDay == 1)
            {
                return $"农历{(isLeap ? "闰" : string.Empty)}{monthName}月";
            }

            var dayName = DayNames[Math.Clamp(lunarDay - 1, 0, DayNames.Length - 1)];
            return $"农历{dayName}";
        }
        catch (ArgumentOutOfRangeException)
        {
            // 超出 .NET 农历支持范围时只显示公历。
            return null;
        }
    }
}
