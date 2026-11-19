using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Tests;

/// <summary>P2-02 保留上限边界：0 / 1 / 200 / 9999、固定豁免与最旧优先。</summary>
public class EvictionPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(9999, 9999)]
    [InlineData(10000, 9999)]
    public void ClampLimit_keeps_documented_range(int input, int expected)
    {
        Assert.Equal(expected, EvictionPolicy.ClampLimit(input));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(201, 0, 0)]
    [InlineData(0, 200, 0)]
    [InlineData(200, 200, 0)]
    [InlineData(201, 200, 1)]
    [InlineData(3, 1, 2)]
    [InlineData(9999, 9999, 0)]
    [InlineData(10000, 9999, 1)]
    public void ExcessCount_covers_boundaries(int nonPinned, int limit, int expected)
    {
        Assert.Equal(expected, EvictionPolicy.ExcessCount(nonPinned, limit));
    }

    [Fact]
    public void SelectForEviction_prefers_oldest_non_pinned_and_never_selected_pinned()
    {
        var items = new[]
        {
            Item(1, 0, pinned: true),
            Item(2, 1, pinned: false),
            Item(3, 2, pinned: true),
            Item(4, 3, pinned: false),
            Item(5, 4, pinned: false),
            Item(6, 5, pinned: false),
        };

        var selected = EvictionPolicy.SelectForEviction(items, limit: 2);

        Assert.Equal(new long[] { 2, 4 }, selected.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void SelectForEviction_ignores_soft_deleted_and_returns_none_when_unlimited()
    {
        var items = new[]
        {
            Item(1, 0, pinned: false, deleted: true),
            Item(2, 1, pinned: false),
        };

        Assert.Empty(EvictionPolicy.SelectForEviction(items, limit: 1));

        var unlimited = new[]
        {
            Item(3, 0, pinned: false),
            Item(4, 1, pinned: false),
        };
        Assert.Empty(EvictionPolicy.SelectForEviction(unlimited, limit: 0));
    }

    [Fact]
    public void SelectForEviction_uses_id_order_for_equal_update_time()
    {
        var items = new[]
        {
            Item(10, 0, pinned: false),
            Item(9, 0, pinned: false),
            Item(11, 0, pinned: false),
        };

        var selected = EvictionPolicy.SelectForEviction(items, limit: 1);

        Assert.Equal(new long[] { 9, 10 }, selected.Select(item => item.Id).ToArray());
    }

    private static ClipboardItem Item(
        long id,
        int seconds,
        bool pinned,
        bool deleted = false) => new()
        {
            Id = id,
            Kind = ClipboardKind.Text,
            TextContent = $"item-{id}",
            NormalizedText = $"item-{id}",
            ContentHash = $"hash-{id}",
            Pinned = pinned,
            PinnedAt = pinned ? Now : null,
            CreatedAt = Now.AddSeconds(seconds),
            UpdatedAt = Now.AddSeconds(seconds),
            DeletedAt = deleted ? Now : null,
        };
}
