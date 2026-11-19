using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.Tests;

/// <summary>P3-04 分类与紧迫度的稳定集合：五种分类、五档紧迫度，分类文案互不重复。</summary>
public class CountdownVisualTests
{
    [Fact]
    public void Category_set_has_five_distinct_labels()
    {
        var categories = Enum.GetValues<CountdownCategory>();
        var labels = categories.Select(CountdownText.CategoryLabel).ToArray();

        Assert.Equal(5, categories.Length);
        Assert.Equal(5, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Urgency_set_has_five_levels_and_summary_covers_all_categories()
    {
        Assert.Equal(5, Enum.GetValues<UrgencyLevel>().Length);

        var summary = CountdownSummaryBuilder.Build(
            [new CountdownItem
            {
                Id = 1,
                Name = "测试",
                Repeat = CountdownRepeat.None,
                AnchorDate = new DateOnly(2026, 12, 1),
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            }],
            new DateOnly(2026, 11, 10));

        Assert.Equal(5, summary.CategoryCounts.Count);
        Assert.All(Enum.GetValues<CountdownCategory>(), category => Assert.True(summary.CategoryCounts.ContainsKey(category)));
    }
}
