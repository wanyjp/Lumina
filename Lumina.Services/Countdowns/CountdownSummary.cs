using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Countdowns;

/// <summary>首页/统计详情的倒计时汇总：总数、分类计数、30 天内数量与最近一项。</summary>
public sealed record CountdownSummary
{
    public required int Total { get; init; }

    public required IReadOnlyDictionary<CountdownCategory, int> CategoryCounts { get; init; }

    /// <summary>未过期且剩余 0–30 天的数量（含今天）。</summary>
    public required int DueWithin30Days { get; init; }

    public CountdownItem? Nearest { get; init; }

    public UrgencySnapshot? NearestSnapshot { get; init; }

    public int CountOf(CountdownCategory category) => CategoryCounts.GetValueOrDefault(category);

    /// <summary>“工作 2 / 生活 1 / 考试 0 / 纪念日 1 / 其他 3”，五种分类恒定顺序。</summary>
    public string CategoryText => string.Join(
        " / ",
        new[]
        {
            CountdownCategory.Work,
            CountdownCategory.Life,
            CountdownCategory.Exam,
            CountdownCategory.Anniversary,
            CountdownCategory.Other,
        }.Select(category => $"{CountdownText.CategoryLabel(category)} {CountOf(category)}"));
}

/// <summary>把倒计时列表汇总成统计详情所需的口径（P3-04）。</summary>
public static class CountdownSummaryBuilder
{
    public const int UpcomingWindowDays = 30;

    public static CountdownSummary Build(IEnumerable<CountdownItem> items, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(items);

        var all = items.ToArray();
        var counts = Enum.GetValues<CountdownCategory>()
            .ToDictionary(category => category, category => all.Count(item => item.Category == category));

        var upcoming = all
            .Select(item => (Item: item, Snapshot: CountdownCalendar.Evaluate(item, today)))
            .Where(entry => entry.Snapshot.NextOccurrence is not null
                && entry.Snapshot.Level != UrgencyLevel.Expired)
            .ToArray();

        var nearest = upcoming
            .OrderBy(entry => entry.Snapshot.DaysRemaining)
            .ThenBy(entry => entry.Item.Id)
            .FirstOrDefault();

        return new CountdownSummary
        {
            Total = all.Length,
            CategoryCounts = counts,
            DueWithin30Days = upcoming.Count(
                entry => entry.Snapshot.DaysRemaining >= 0
                    && entry.Snapshot.DaysRemaining <= UpcomingWindowDays),
            Nearest = nearest.Item,
            NearestSnapshot = nearest.Snapshot,
        };
    }
}
