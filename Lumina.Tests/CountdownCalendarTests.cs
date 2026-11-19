using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Tests;

/// <summary>
/// P3-03 倒计时日期口径：短月与月底、2/29、每周当天、重复项不为负、五档紧迫度、
/// 进度条比例、锚点单存与切换清空。
/// </summary>
public class CountdownCalendarTests
{
    [Fact]
    public void Monthly_day_31_resolves_to_last_day_of_short_month()
    {
        var item = Item(CountdownRepeat.Monthly, day: 31);
        var occurrence = CountdownCalendar.Resolve(item, new DateOnly(2026, 2, 10));

        Assert.True(occurrence.IsValid);
        Assert.Equal(new DateOnly(2026, 2, 28), occurrence.Date);
        Assert.Equal(18, occurrence.DaysRemaining);
        Assert.False(occurrence.IsExpired);
    }

    [Fact]
    public void Monthly_month_end_always_uses_last_day()
    {
        var item = Item(CountdownRepeat.Monthly, monthEnd: true);

        Assert.Equal(
            new DateOnly(2026, 2, 28),
            CountdownCalendar.Resolve(item, new DateOnly(2026, 2, 1)).Date);
        Assert.Equal(
            new DateOnly(2026, 4, 30),
            CountdownCalendar.Resolve(item, new DateOnly(2026, 4, 30)).Date);
        Assert.Equal(
            new DateOnly(2026, 5, 31),
            CountdownCalendar.Resolve(item, new DateOnly(2026, 5, 1)).Date);
    }

    [Fact]
    public void Yearly_feb_29_clamps_to_feb_28_in_common_years()
    {
        var item = Item(CountdownRepeat.Yearly, month: 2, day: 29);

        Assert.Equal(
            new DateOnly(2027, 2, 28),
            CountdownCalendar.Resolve(item, new DateOnly(2026, 11, 10)).Date);
        Assert.Equal(
            new DateOnly(2028, 2, 29),
            CountdownCalendar.Resolve(item, new DateOnly(2028, 1, 1)).Date);
    }

    [Fact]
    public void Weekly_anchor_hits_today_with_zero_days()
    {
        var item = Item(CountdownRepeat.Weekly, weekday: (int)new DateOnly(2026, 11, 10).DayOfWeek);
        var occurrence = CountdownCalendar.Resolve(item, new DateOnly(2026, 11, 10));

        Assert.Equal(new DateOnly(2026, 11, 10), occurrence.Date);
        Assert.Equal(0, occurrence.DaysRemaining);
        Assert.False(occurrence.IsExpired);
    }

    [Fact]
    public void Repeated_items_never_return_negative_days()
    {
        var monthly = Item(CountdownRepeat.Monthly, day: 31);
        var yearly = Item(CountdownRepeat.Yearly, month: 2, day: 29);
        var weekly = Item(CountdownRepeat.Weekly, weekday: 1);

        foreach (var (item, today) in new[]
                 {
                     (monthly, new DateOnly(2026, 2, 28)),
                     (yearly, new DateOnly(2026, 3, 1)),
                     (weekly, new DateOnly(2026, 11, 10)),
                 })
        {
            var occurrence = CountdownCalendar.Resolve(item, today);
            Assert.True(occurrence.IsValid);
            Assert.True(occurrence.DaysRemaining >= 0);
            Assert.False(occurrence.IsExpired);
            Assert.NotNull(CountdownCalendar.NextOccurrence(item, today));
        }
    }

    [Fact]
    public void Once_past_keeps_negative_days_and_is_expired()
    {
        var item = Item(CountdownRepeat.None, anchorDate: new DateOnly(2026, 11, 1));
        var today = new DateOnly(2026, 11, 10);

        var occurrence = CountdownCalendar.Resolve(item, today);
        Assert.True(occurrence.IsValid);
        Assert.Equal(new DateOnly(2026, 11, 1), occurrence.Date);
        Assert.Equal(-9, occurrence.DaysRemaining);
        Assert.True(occurrence.IsExpired);
        Assert.Null(CountdownCalendar.NextOccurrence(item, today));

        var snapshot = CountdownCalendar.Evaluate(item, today);
        Assert.Equal(UrgencyLevel.Expired, snapshot.Level);
        Assert.Equal(-9, snapshot.DaysRemaining);
        Assert.False(snapshot.ShouldBreathe);
        Assert.Equal(new DateOnly(2026, 11, 1), snapshot.NextOccurrence);
    }

    [Theory]
    [InlineData(0, UrgencyLevel.Urgent)]
    [InlineData(3, UrgencyLevel.Urgent)]
    [InlineData(4, UrgencyLevel.Hot)]
    [InlineData(7, UrgencyLevel.Hot)]
    [InlineData(8, UrgencyLevel.Warm)]
    [InlineData(30, UrgencyLevel.Warm)]
    [InlineData(31, UrgencyLevel.Calm)]
    public void Urgency_thresholds_are_five_levels(int days, UrgencyLevel expected)
    {
        Assert.Equal(expected, CountdownCalendar.LevelFor(days));
    }

    [Fact]
    public void Urgent_level_breathes_and_others_do_not()
    {
        var urgent = CountdownCalendar.Evaluate(
            Item(CountdownRepeat.None, anchorDate: new DateOnly(2026, 11, 12)),
            new DateOnly(2026, 11, 10));
        var calm = CountdownCalendar.Evaluate(
            Item(CountdownRepeat.None, anchorDate: new DateOnly(2027, 1, 1)),
            new DateOnly(2026, 11, 10));

        Assert.Equal(UrgencyLevel.Urgent, urgent.Level);
        Assert.True(urgent.ShouldBreathe);
        Assert.Equal(UrgencyLevel.Calm, calm.Level);
        Assert.False(calm.ShouldBreathe);
    }

    [Fact]
    public void Progress_is_full_today_and_clamped_within_window()
    {
        Assert.Equal(1d, CountdownCalendar.ProgressFor(CountdownRepeat.Yearly, 0), 6);
        Assert.Equal(4d / 7d, CountdownCalendar.ProgressFor(CountdownRepeat.Weekly, 3), 6);
        Assert.Equal(0.08d, CountdownCalendar.ProgressFor(CountdownRepeat.Yearly, 400), 6);
        Assert.Equal(0.08d, CountdownCalendar.ProgressFor(CountdownRepeat.None, 365), 6);
    }

    [Fact]
    public void Invalid_anchor_yields_invalid_snapshot_without_throwing()
    {
        var item = Item(CountdownRepeat.Weekly, weekday: null);

        var occurrence = CountdownCalendar.Resolve(item, new DateOnly(2026, 11, 10));
        Assert.False(occurrence.IsValid);
        Assert.Null(occurrence.Date);

        var snapshot = CountdownCalendar.Evaluate(item, new DateOnly(2026, 11, 10));
        Assert.Equal(UrgencyLevel.Calm, snapshot.Level);
        Assert.Equal(0, snapshot.DaysRemaining);
        Assert.Equal(0d, snapshot.ProgressRatio);
        Assert.Null(snapshot.NextOccurrence);
    }

    [Theory]
    [InlineData(CountdownRepeat.Monthly, null, false)]
    [InlineData(CountdownRepeat.Monthly, 32, false)]
    [InlineData(CountdownRepeat.Yearly, null, true)]
    public void Implausible_anchors_are_invalid(CountdownRepeat repeat, int? day, bool monthEnd)
    {
        var item = Item(repeat, day: day, month: repeat == CountdownRepeat.Yearly ? 13 : null, monthEnd: monthEnd);

        Assert.False(CountdownCalendar.Resolve(item, new DateOnly(2026, 11, 10)).IsValid);
    }

    [Fact]
    public void With_anchor_keeps_only_the_anchor_for_current_repeat()
    {
        var date = new DateOnly(2026, 11, 10); // 周二

        var weekly = CountdownCalendar.WithAnchor(Item(CountdownRepeat.Weekly), date);
        Assert.Equal(2, weekly.AnchorWeekday);
        Assert.Null(weekly.AnchorDate);
        Assert.Null(weekly.AnchorDay);
        Assert.Null(weekly.AnchorMonth);
        Assert.False(weekly.AnchorMonthEnd);

        var monthly = CountdownCalendar.WithAnchor(Item(CountdownRepeat.Monthly), date);
        Assert.Equal(10, monthly.AnchorDay);
        Assert.Null(monthly.AnchorDate);
        Assert.Null(monthly.AnchorWeekday);
        Assert.False(monthly.AnchorMonthEnd);

        var monthEnd = CountdownCalendar.WithAnchor(Item(CountdownRepeat.Monthly), date, monthEnd: true);
        Assert.Null(monthEnd.AnchorDay);
        Assert.True(monthEnd.AnchorMonthEnd);

        var yearly = CountdownCalendar.WithAnchor(Item(CountdownRepeat.Yearly), date);
        Assert.Equal(11, yearly.AnchorMonth);
        Assert.Equal(10, yearly.AnchorDay);
        Assert.Null(yearly.AnchorDate);

        var once = CountdownCalendar.WithAnchor(Item(CountdownRepeat.None), date);
        Assert.Equal(date, once.AnchorDate);
        Assert.Null(once.AnchorDay);
    }

    [Fact]
    public void With_repeat_clears_old_anchor_and_keeps_same_repeat_untouched()
    {
        var monthly = CountdownCalendar.WithAnchor(Item(CountdownRepeat.Monthly), new DateOnly(2026, 11, 10));

        var switched = CountdownCalendar.WithRepeat(monthly, CountdownRepeat.Yearly);
        Assert.Equal(CountdownRepeat.Yearly, switched.Repeat);
        Assert.Null(switched.AnchorDay);
        Assert.Null(switched.AnchorMonth);
        Assert.Null(switched.AnchorWeekday);
        Assert.Null(switched.AnchorDate);
        Assert.False(switched.AnchorMonthEnd);

        var unchanged = CountdownCalendar.WithRepeat(monthly, CountdownRepeat.Monthly);
        Assert.Equal(10, unchanged.AnchorDay);
    }

    private static CountdownItem Item(
        CountdownRepeat repeat,
        int? weekday = null,
        int? day = null,
        int? month = null,
        bool monthEnd = false,
        DateOnly? anchorDate = null) =>
        new()
        {
            Id = 1,
            Name = "测试倒计时",
            Repeat = repeat,
            Reminder = ReminderOffset.Day1,
            AnchorWeekday = weekday,
            AnchorDay = day,
            AnchorMonth = month,
            AnchorMonthEnd = monthEnd,
            AnchorDate = anchorDate,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
}
