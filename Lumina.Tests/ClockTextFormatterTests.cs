using Lumina.Services.Clock;

namespace Lumina.Tests;

/// <summary>P1-01 时间制式、现代时段、十二时辰与农历格式化。</summary>
public class ClockTextFormatterTests
{
    [Fact]
    public void Twenty_four_hour_mode_keeps_24_hour_text_and_shows_period()
    {
        Assert.Equal("00:00:00", ClockTextFormatter.FormatTime(At(0), use12Hour: false));
        Assert.Equal("23:00:00", ClockTextFormatter.FormatTime(At(23), use12Hour: false));
        Assert.Equal("晚上", ClockTextFormatter.FormatPeriod(At(0), use12Hour: false, periodMode: "modern"));
        Assert.Equal("上午", ClockTextFormatter.FormatPeriod(At(8), use12Hour: false, periodMode: "modern"));
        Assert.Equal("辰时", ClockTextFormatter.FormatPeriod(At(8), use12Hour: false, periodMode: "ancient"));
        Assert.Null(ClockTextFormatter.FormatPeriod(At(8), use12Hour: false, periodMode: "none"));
    }

    [Fact]
    public void Twelve_hour_mode_covers_midnight_noon_and_night()
    {
        Assert.Equal("12:00:00", ClockTextFormatter.FormatTime(At(0), use12Hour: true));
        Assert.Equal("上午", ClockTextFormatter.FormatPeriod(At(8), use12Hour: true, periodMode: "modern"));
        Assert.Equal("中午", ClockTextFormatter.FormatPeriod(At(12), use12Hour: true, periodMode: "modern"));
        Assert.Equal("11:00:00", ClockTextFormatter.FormatTime(At(23), use12Hour: true));
        Assert.Equal("晚上", ClockTextFormatter.FormatPeriod(At(23), use12Hour: true, periodMode: "modern"));
        Assert.Null(ClockTextFormatter.FormatPeriod(At(8), use12Hour: true, periodMode: "none"));
    }

    [Fact]
    public void Modern_period_boundaries_match_prototype()
    {
        Assert.Equal("晚上", ClockTextFormatter.ModernPeriodText(4));
        Assert.Equal("清晨", ClockTextFormatter.ModernPeriodText(5));
        Assert.Equal("清晨", ClockTextFormatter.ModernPeriodText(7));
        Assert.Equal("上午", ClockTextFormatter.ModernPeriodText(8));
        Assert.Equal("上午", ClockTextFormatter.ModernPeriodText(11));
        Assert.Equal("中午", ClockTextFormatter.ModernPeriodText(12));
        Assert.Equal("中午", ClockTextFormatter.ModernPeriodText(13));
        Assert.Equal("下午", ClockTextFormatter.ModernPeriodText(14));
        Assert.Equal("下午", ClockTextFormatter.ModernPeriodText(17));
        Assert.Equal("晚上", ClockTextFormatter.ModernPeriodText(18));
    }

    [Fact]
    public void Ancient_mode_maps_to_twelve_shichen()
    {
        Assert.Equal("子时", ClockTextFormatter.ShichenText(At(0)));
        Assert.Equal("丑时", ClockTextFormatter.ShichenText(At(1)));
        Assert.Equal("午时", ClockTextFormatter.ShichenText(At(12)));
        Assert.Equal("子时", ClockTextFormatter.ShichenText(At(23)));
    }

    [Fact]
    public void Lunar_text_matches_known_dates()
    {
        Assert.Equal("农历廿七", ClockTextFormatter.LunarText(new DateOnly(2026, 11, 5)));
        Assert.Equal("农历正月", ClockTextFormatter.LunarText(new DateOnly(2026, 2, 17)));
    }

    [Fact]
    public void Display_line_contains_weekday_and_lunar()
    {
        var local = At(12);
        Assert.Contains("星期四", ClockTextFormatter.FormatDate(local), StringComparison.Ordinal);
        Assert.Contains("农历廿七", ClockTextFormatter.FormatShortDate(local, "农历廿七"), StringComparison.Ordinal);
    }

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 11, 5, hour, minute, 0, TimeSpan.FromHours(8));
}
