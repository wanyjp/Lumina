using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Reminders;

namespace Lumina.Tests;

/// <summary>
/// P3-03 倒计时下次发生日：周/月/年滚动；每月短月收敛到当月最后一天，
/// 月底锚点恒取最后一天，每年 2/29 在平年收敛到 2/28。
/// </summary>
public class CountdownOccurrenceCalculatorTests
{
    [Fact]
    public void Weekly_rolls_to_next_anchor_weekday()
    {
        var item = Item(CountdownRepeat.Weekly, weekday: 1); // 周一
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 11, 6)); // 周五

        Assert.Equal(new DateOnly(2026, 11, 9), next);
    }

    [Fact]
    public void Weekly_anchor_today_returns_today()
    {
        var item = Item(CountdownRepeat.Weekly, weekday: 5); // 周五
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 11, 6));

        Assert.Equal(new DateOnly(2026, 11, 6), next);
    }

    [Fact]
    public void Monthly_clamps_missing_day_31_to_last_day()
    {
        var item = Item(CountdownRepeat.Monthly, day: 31);
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), next);
    }

    [Fact]
    public void Monthly_month_end_uses_last_day()
    {
        var item = Item(CountdownRepeat.Monthly, monthEnd: true);
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), next);
    }

    [Fact]
    public void Monthly_day_31_rolls_to_next_month_after_the_last_day()
    {
        var item = Item(CountdownRepeat.Monthly, day: 31);
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 2, 28));

        Assert.Equal(new DateOnly(2026, 2, 28), next);

        var after = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 3, 31), after);
    }

    [Fact]
    public void Yearly_clamps_feb_29_to_feb_28_in_non_leap_years()
    {
        var item = Item(CountdownRepeat.Yearly, month: 2, day: 29);
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 3, 1));

        Assert.Equal(new DateOnly(2027, 2, 28), next);
    }

    [Fact]
    public void Yearly_keeps_feb_29_in_leap_years()
    {
        var item = Item(CountdownRepeat.Yearly, month: 2, day: 29);
        var next = CountdownOccurrenceCalculator.Next(item, new DateOnly(2028, 1, 1));

        Assert.Equal(new DateOnly(2028, 2, 29), next);
    }

    [Fact]
    public void None_returns_anchor_only_when_not_past()
    {
        var item = Item(CountdownRepeat.None, anchorDate: new DateOnly(2026, 11, 6));

        Assert.Equal(
            new DateOnly(2026, 11, 6),
            CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 11, 6)));
        Assert.Null(CountdownOccurrenceCalculator.Next(item, new DateOnly(2026, 11, 7)));
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
