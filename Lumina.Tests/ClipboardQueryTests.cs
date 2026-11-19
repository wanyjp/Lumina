using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Tests;

/// <summary>P2-03 列表查询：页签、计数、搜索覆盖面与标签口径。</summary>
public class ClipboardQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Tabs_are_fixed_order_and_cover_five_formats_plus_pinned()
    {
        Assert.Equal(
            [
                ClipboardFilter.All,
                ClipboardFilter.Text,
                ClipboardFilter.Link,
                ClipboardFilter.Html,
                ClipboardFilter.Image,
                ClipboardFilter.Pinned,
            ],
            ClipboardFilters.Tabs);

        Assert.Equal("HTML", ClipboardFilters.Label(ClipboardFilter.Html));
        Assert.Equal(ClipboardFilter.Pinned, ClipboardFilters.Parse("pinned"));
        Assert.Equal(ClipboardFilter.All, ClipboardFilters.Parse("unknown"));
    }

    [Fact]
    public void Filter_matches_kind_and_pinned_independently()
    {
        var text = Item(1, ClipboardKind.Text);
        var pinnedImage = Item(2, ClipboardKind.Image) with { Pinned = true };

        Assert.True(ClipboardFilters.Matches(text, ClipboardFilter.All));
        Assert.True(ClipboardFilters.Matches(text, ClipboardFilter.Text));
        Assert.False(ClipboardFilters.Matches(text, ClipboardFilter.Image));

        // 固定页签按 pinned 判定，与格式无关；固定条目仍属于其格式页签。
        Assert.True(ClipboardFilters.Matches(pinnedImage, ClipboardFilter.Pinned));
        Assert.True(ClipboardFilters.Matches(pinnedImage, ClipboardFilter.Image));
        Assert.False(ClipboardFilters.Matches(text, ClipboardFilter.Pinned));
    }

    [Fact]
    public void Counts_summarize_formats_and_pinned()
    {
        var items = new[]
        {
            Item(1, ClipboardKind.Text),
            Item(2, ClipboardKind.Text) with { Pinned = true },
            Item(3, ClipboardKind.Link),
            Item(4, ClipboardKind.Html),
            Item(5, ClipboardKind.Image),
            Item(6, ClipboardKind.File),
        };

        var counts = ClipboardCounts.From(items);

        Assert.Equal(6, counts.All);
        Assert.Equal(2, counts.Text);
        Assert.Equal(1, counts.Link);
        Assert.Equal(1, counts.Html);
        Assert.Equal(1, counts.Image);
        Assert.Equal(1, counts.File);
        Assert.Equal(1, counts.Pinned);
        Assert.Equal(2, counts.For(ClipboardFilter.Text));
        Assert.Equal(6, counts.For(ClipboardFilter.All));
    }

    [Fact]
    public void Search_covers_body_note_tags_source_and_url()
    {
        var item = Item(1, ClipboardKind.Text) with
        {
            TextContent = "季度复盘 会议纪要",
            NormalizedText = "季度复盘 会议纪要",
            Note = "下周一同步",
            Tags = ["工作", "Q4"],
            SourceWindow = "Notepad · notepad.exe",
        };

        Assert.True(ClipboardSearch.Matches(item, "复盘"));
        Assert.True(ClipboardSearch.Matches(item, "同步"));
        Assert.True(ClipboardSearch.Matches(item, "q4"));
        Assert.True(ClipboardSearch.Matches(item, "NOTEPAD"));
        Assert.False(ClipboardSearch.Matches(item, "不存在"));
        Assert.True(ClipboardSearch.Matches(item, "   "));

        var link = Item(2, ClipboardKind.Link) with { Url = "https://example.com/a" };
        Assert.True(ClipboardSearch.Matches(link, "example.com"));
    }

    [Fact]
    public void Search_normalizes_query_and_caps_length()
    {
        Assert.Equal(string.Empty, ClipboardSearch.Normalize(null));
        Assert.Equal(string.Empty, ClipboardSearch.Normalize("   "));
        Assert.Equal("关键词", ClipboardSearch.Normalize("  关键词  "));

        var longQuery = new string('a', ClipboardSearch.MaxQueryLength + 20);
        Assert.Equal(ClipboardSearch.MaxQueryLength, ClipboardSearch.Normalize(longQuery).Length);
        Assert.False(ClipboardSearch.IsActive("  "));
    }

    [Fact]
    public void Build_applies_search_then_filter_and_tag()
    {
        var items = new[]
        {
            Item(1, ClipboardKind.Text) with { TextContent = "alpha", Tags = ["工作"] },
            Item(2, ClipboardKind.Text) with { TextContent = "beta" },
            Item(3, ClipboardKind.Link) with { TextContent = "alpha link", Url = "https://a" },
        };

        var all = ClipboardQuery.Build(items, ClipboardFilter.All);
        Assert.Equal(3, all.Items.Count);
        Assert.Equal(3, all.TotalCount);

        var textOnly = ClipboardQuery.Build(items, ClipboardFilter.Text);
        Assert.Equal([1L, 2L], textOnly.Items.Select(item => item.Id));

        var searched = ClipboardQuery.Build(items, ClipboardFilter.All, "alpha");
        Assert.Equal([1L, 3L], searched.Items.Select(item => item.Id));
        Assert.Equal(2, searched.Counts.All);
        Assert.Equal(1, searched.Counts.Text);

        var tagged = ClipboardQuery.Build(items, ClipboardFilter.All, null, "工作");
        Assert.Equal([1L], tagged.Items.Select(item => item.Id));
    }

    [Fact]
    public void Build_collects_tags_by_frequency_then_name()
    {
        var items = new[]
        {
            Item(1, ClipboardKind.Text) with { Tags = ["工作", "Q4"] },
            Item(2, ClipboardKind.Text) with { Tags = ["工作", "alpha"] },
            Item(3, ClipboardKind.Text) with { Tags = ["q4"] },
        };

        var view = ClipboardQuery.Build(items, ClipboardFilter.All);

        // 工作 2 次、Q4 2 次（大小写合并，保留首次写法）、alpha 1 次；同次数按字典序。
        Assert.Equal(["Q4", "工作", "alpha"], view.Tags);
    }

    [Fact]
    public void Build_reports_empty_view_when_no_items()
    {
        var view = ClipboardQuery.Build(Array.Empty<ClipboardItem>(), ClipboardFilter.Pinned);

        Assert.Empty(view.Items);
        Assert.Empty(view.Tags);
        Assert.Equal(0, view.TotalCount);
        Assert.Equal(0, view.Counts.Pinned);
    }

    [Fact]
    public void Tag_policy_limits_count_and_length()
    {
        var many = new[] { "一", "二", "三", "四", "五", "六", "七" };
        var kept = ClipboardTagPolicy.NormalizeAll(many, out var dropped);

        Assert.Equal(5, kept.Count);
        Assert.Equal(2, dropped);

        var longTag = ClipboardTagPolicy.Normalize("这是一个超过十二个字的标签名称");
        Assert.Equal(ClipboardTagPolicy.MaxLength, longTag.Length);
        Assert.Equal("这是一个超过十二个字的标", longTag);
    }

    [Fact]
    public void Tag_policy_deduplicates_case_insensitively_and_trims()
    {
        var kept = ClipboardTagPolicy.NormalizeAll(["  工作 ", "工作", "WORK", "work"], out var dropped);

        Assert.Equal(["工作", "WORK"], kept);
        Assert.Equal(2, dropped);
    }

    [Fact]
    public void Tag_policy_parses_free_input_with_common_separators()
    {
        var tags = ClipboardTagPolicy.FromInput("工作, 重要、 Q4  2026；归档", out var dropped);

        Assert.Equal(["工作", "重要", "Q4", "2026", "归档"], tags);
        Assert.Equal(0, dropped);
        Assert.Equal("工作，重要", ClipboardTagPolicy.ToInput(tags.Take(2)));
    }

    [Fact]
    public void Tag_policy_ignores_blank_and_empty_input()
    {
        Assert.Empty(ClipboardTagPolicy.NormalizeAll(null, out var dropped));
        Assert.Equal(0, dropped);
        Assert.Empty(ClipboardTagPolicy.FromInput("   ", out _));
        Assert.Empty(ClipboardTagPolicy.FromInput(null, out _));
        Assert.Equal(string.Empty, ClipboardTagPolicy.Normalize("   "));
    }

    private static ClipboardItem Item(long id, ClipboardKind kind) => new()
    {
        Id = id,
        Kind = kind,
        TextContent = $"内容-{id}",
        NormalizedText = $"内容-{id}",
        ContentHash = $"hash-{id}",
        CreatedAt = Now.AddSeconds((int)id),
        UpdatedAt = Now.AddSeconds((int)id),
    };
}
