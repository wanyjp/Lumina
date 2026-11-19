using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.Tests;

/// <summary>P3-04 统计详情：倒计时总数、分类计数、30 天内与最近一项。</summary>
public class CountdownSummaryTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void Summary_counts_categories_upcoming_and_nearest()
    {
        var items = new[]
        {
            Once(1, "三天后", CountdownCategory.Work, Today.AddDays(3)),
            Once(2, "四十天后", CountdownCategory.Life, Today.AddDays(40)),
            Once(3, "已过期", CountdownCategory.Exam, Today.AddDays(-2)),
            Once(4, "今天", CountdownCategory.Anniversary, Today),
            new CountdownItem
            {
                Id = 5,
                Name = "无效",
                Category = CountdownCategory.Other,
                Repeat = CountdownRepeat.Yearly,
                AnchorMonth = 2,
                AnchorDay = 30,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            },
        };

        var summary = CountdownSummaryBuilder.Build(items, Today);

        Assert.Equal(5, summary.Total);
        Assert.Equal(1, summary.CountOf(CountdownCategory.Work));
        Assert.Equal(1, summary.CountOf(CountdownCategory.Life));
        Assert.Equal(1, summary.CountOf(CountdownCategory.Exam));
        Assert.Equal(1, summary.CountOf(CountdownCategory.Anniversary));
        Assert.Equal(1, summary.CountOf(CountdownCategory.Other));
        Assert.Equal(2, summary.DueWithin30Days);
        Assert.Equal("今天", summary.Nearest?.Name);
        Assert.Equal(0, summary.NearestSnapshot?.DaysRemaining);
        Assert.Equal(
            "工作 1 / 生活 1 / 考试 1 / 纪念日 1 / 其他 1",
            summary.CategoryText);
    }

    [Fact]
    public void Thirty_day_window_is_inclusive_and_nearest_breaks_ties_by_id()
    {
        var items = new[]
        {
            Once(7, "三十一天", CountdownCategory.Other, Today.AddDays(31)),
            Once(6, "三十天", CountdownCategory.Other, Today.AddDays(30)),
            Once(9, "同天 B", CountdownCategory.Other, Today.AddDays(5)),
            Once(8, "同天 A", CountdownCategory.Other, Today.AddDays(5)),
        };

        var summary = CountdownSummaryBuilder.Build(items, Today);

        // 30 天含当天边界：30 天与两个 5 天项计入，31 天不计入。
        Assert.Equal(3, summary.DueWithin30Days);
        Assert.Equal("同天 A", summary.Nearest?.Name);
        Assert.Equal(8, summary.Nearest?.Id);
    }

    [Fact]
    public void Empty_list_returns_zero_and_all_five_categories()
    {
        var summary = CountdownSummaryBuilder.Build([], Today);

        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.DueWithin30Days);
        Assert.Null(summary.Nearest);
        Assert.Null(summary.NearestSnapshot);
        Assert.Equal(5, summary.CategoryCounts.Count);
        Assert.Equal("工作 0 / 生活 0 / 考试 0 / 纪念日 0 / 其他 0", summary.CategoryText);
    }

    private static CountdownItem Once(long id, string name, CountdownCategory category, DateOnly date) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Repeat = CountdownRepeat.None,
            AnchorDate = date,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
}
