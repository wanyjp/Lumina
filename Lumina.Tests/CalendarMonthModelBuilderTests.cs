using Lumina.Data.Schedules;
using Lumina.Data.Settings;
using Lumina.Services.Calendar;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-05 月历模型：补位、周起始、农历/节气、节假日与降级。</summary>
public class CalendarMonthModelBuilderTests
{
    [Fact]
    public void Builds_six_weeks_with_leading_and_trailing_dates()
    {
        var model = CalendarMonthModelBuilder.Build(
            2026, 11, mondayFirst: true, today: new DateOnly(2026, 11, 6));

        Assert.Equal(CalendarMonthModelBuilder.CellCount, model.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), model.Days[0].Date);
        Assert.Equal(DayOfWeek.Monday, model.Days[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 12, 6), model.Days[^1].Date);
        Assert.Equal(30, model.Days.Count(day => day.IsCurrentMonth));
        Assert.True(model.Days.Single(day => day.Date == new DateOnly(2026, 11, 6)).IsToday);
        Assert.DoesNotContain(model.Days, day => day.Date == new DateOnly(2026, 11, 5) && day.IsToday);
    }

    [Fact]
    public void Week_start_option_reorders_the_grid()
    {
        var mondayFirst = CalendarMonthModelBuilder.Build(
            2026, 11, mondayFirst: true, today: new DateOnly(2026, 11, 6));
        var sundayFirst = CalendarMonthModelBuilder.Build(
            2026, 11, mondayFirst: false, today: new DateOnly(2026, 11, 6));

        Assert.Equal(new DateOnly(2026, 10, 26), mondayFirst.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 1), sundayFirst.Days[0].Date);
        Assert.Equal(DayOfWeek.Sunday, sundayFirst.Days[0].Date.DayOfWeek);
    }

    [Fact]
    public void Solar_terms_replace_lunar_text()
    {
        var february = CalendarMonthModelBuilder.Build(
            2026, 2, mondayFirst: true, today: new DateOnly(2026, 2, 17));
        var november = CalendarMonthModelBuilder.Build(
            2026, 11, mondayFirst: true, today: new DateOnly(2026, 11, 6));
        var december = CalendarMonthModelBuilder.Build(
            2026, 12, mondayFirst: true, today: new DateOnly(2026, 12, 22));

        Assert.Equal("立春", Day(february, new DateOnly(2026, 2, 4)).LunarText);
        Assert.Equal("立冬", Day(november, new DateOnly(2026, 11, 7)).LunarText);
        Assert.Equal("冬至", Day(december, new DateOnly(2026, 12, 22)).LunarText);
        Assert.Equal("廿八", Day(november, new DateOnly(2026, 11, 6)).LunarText);
    }

    [Fact]
    public void Spring_festival_lunar_text_is_correct()
    {
        Assert.Equal("廿九", LunarCalendarText.DayText(new DateOnly(2026, 2, 16)));
        Assert.Equal("正月", LunarCalendarText.DayText(new DateOnly(2026, 2, 17)));
        Assert.Null(LunarCalendarText.DayText(new DateOnly(1900, 1, 1)));
    }

    [Fact]
    public void Build_covers_leap_year_february_month_end_and_year_boundary()
    {
        var leapFebruary = CalendarMonthModelBuilder.Build(
            2024, 2, mondayFirst: true, today: new DateOnly(2024, 2, 29));
        var december = CalendarMonthModelBuilder.Build(
            2026, 12, mondayFirst: true, today: new DateOnly(2026, 12, 31));

        Assert.Contains(leapFebruary.Days, day => day.Date == new DateOnly(2024, 2, 29));
        Assert.Equal(29, leapFebruary.Days.Count(day => day.IsCurrentMonth));
        Assert.Equal(42, leapFebruary.Days.Count);
        Assert.Contains(december.Days, day => day.Date.Year == 2027 && day.Date.Month == 1);
        Assert.Equal(31, december.Days.Count(day => day.IsCurrentMonth));
    }

    [Fact]
    public void Bars_provider_is_applied_to_each_day()
    {
        var model = CalendarMonthModelBuilder.Build(
            2026, 11,
            mondayFirst: true,
            today: new DateOnly(2026, 11, 6),
            barsProvider: date => date.Day == 6
                ? new[] { new CalendarBarModel("示例条带", IsStart: true, IsEnd: false, Level: 0) }
                : Array.Empty<CalendarBarModel>());

        var today = Day(model, new DateOnly(2026, 11, 6));
        Assert.Single(today.Bars);
        Assert.Equal("示例条带", today.Bars[0].Title);
        Assert.Empty(Day(model, new DateOnly(2026, 11, 7)).Bars);
    }

    [Fact]
    public void Holiday_calendar_matches_2026_notice()
    {
        var calendar = CnHolidayCalendar.Default;

        Assert.True(calendar.HasData(2026));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2026, 1, 1)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2026, 1, 3)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2026, 2, 15)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2026, 2, 23)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2026, 10, 7)));
        Assert.False(calendar.IsLegalHoliday(new DateOnly(2026, 2, 24)));

        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 1, 4)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 2, 14)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 2, 28)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 5, 9)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 9, 20)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2026, 10, 10)));

        Assert.True(calendar.IsWorkingDay(new DateOnly(2026, 2, 14)));
        Assert.False(calendar.IsWorkingDay(new DateOnly(2026, 2, 16)));
        Assert.False(calendar.IsWorkingDay(new DateOnly(2026, 3, 7)));
        Assert.True(calendar.IsWorkingDay(new DateOnly(2026, 3, 6)));
    }

    [Fact]
    public void Holiday_calendar_matches_2027_notice()
    {
        var calendar = CnHolidayCalendar.Default;

        Assert.True(calendar.HasData(2027));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2027, 1, 1)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2027, 2, 5)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2027, 2, 13)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2027, 6, 9)));
        Assert.True(calendar.IsLegalHoliday(new DateOnly(2027, 10, 7)));
        Assert.False(calendar.IsLegalHoliday(new DateOnly(2027, 2, 14)));

        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2027, 1, 31)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2027, 2, 14)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2027, 5, 8)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2027, 9, 26)));
        Assert.True(calendar.IsMakeupWorkday(new DateOnly(2027, 10, 9)));

        Assert.True(calendar.IsWorkingDay(new DateOnly(2027, 10, 9)));
        Assert.False(calendar.IsWorkingDay(new DateOnly(2027, 10, 1)));
        Assert.False(calendar.IsWorkingDay(new DateOnly(2027, 10, 3)));
        Assert.True(calendar.IsWorkingDay(new DateOnly(2027, 10, 8)));
    }

    [Fact]
    public void Unlisted_year_degrades_to_weekday_rules()
    {
        var calendar = CnHolidayCalendar.Default;

        Assert.False(calendar.HasData(2028));
        Assert.False(calendar.IsLegalHoliday(new DateOnly(2028, 10, 1)));
        Assert.False(calendar.IsWorkingDay(new DateOnly(2028, 10, 1)));
        Assert.True(calendar.IsWorkingDay(new DateOnly(2028, 10, 2)));
    }

    [Fact]
    public void Calendar_service_uses_week_start_preference()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var service = new CalendarService(settings, new ScheduleRepository(temp.Database));

        var mondayFirst = service.BuildMonth(2026, 11);
        Assert.Equal(DayOfWeek.Monday, mondayFirst.Days[0].Date.DayOfWeek);

        settings.Set(PreferenceKeys.WeekStart, 0);
        var sundayFirst = service.BuildMonth(2026, 11);
        Assert.Equal(DayOfWeek.Sunday, sundayFirst.Days[0].Date.DayOfWeek);
    }

    [Fact]
    public void Out_of_range_year_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalendarMonthModelBuilder.Build(1900, 1, mondayFirst: true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalendarMonthModelBuilder.Build(2026, 13, mondayFirst: true));
    }

    private static CalendarDayModel Day(CalendarMonthModel model, DateOnly date) =>
        model.Days.Single(day => day.Date == date);
}
