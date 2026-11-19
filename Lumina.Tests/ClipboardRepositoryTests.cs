using Lumina.Data.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;

namespace Lumina.Tests;

/// <summary>P2-01 剪贴板入库与时间窗去重合并。</summary>
public class ClipboardRepositoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Save_inserts_first_and_merges_duplicate_within_window()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var first = repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);
        Assert.False(first.Merged);
        Assert.True(first.Item.Id > 0);
        Assert.Equal(1, first.Item.DuplicateCount);

        var second = repository.Save(
            TextItem() with { SourceWindow = "Chrome · chrome.exe" },
            Now.AddSeconds(2),
            ClipboardCapturePolicyWindow,
            0);

        Assert.True(second.Merged);
        Assert.Equal(first.Item.Id, second.Item.Id);
        Assert.Equal(2, second.Item.DuplicateCount);
        Assert.Equal("Chrome · chrome.exe", second.Item.SourceWindow);
        Assert.Equal(Now.AddSeconds(2), second.Item.UpdatedAt);
        Assert.Single(repository.QueryRecent(10));
    }

    [Fact]
    public void Save_outside_window_inserts_new_row()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var first = repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);
        var second = repository.Save(TextItem(), Now.AddSeconds(6), ClipboardCapturePolicyWindow, 0);

        Assert.False(second.Merged);
        Assert.NotEqual(first.Item.Id, second.Item.Id);
        Assert.Equal(2, temp.CountRows("clipboard_item"));
    }

    [Fact]
    public void Save_different_hash_keeps_separate_rows_and_orders_by_update_time()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);
        var later = repository.Save(
            TextItem() with
            {
                TextContent = "另一条",
                NormalizedText = "另一条",
                ContentHash = "hash-2",
            },
            Now.AddSeconds(10),
            ClipboardCapturePolicyWindow,
            0);

        var recent = repository.QueryRecent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(later.Item.Id, recent[0].Id);
        Assert.Equal(ClipboardKind.Text, recent[0].Kind);
    }

    [Fact]
    public void Save_image_item_round_trips_media_fields()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var image = new ClipboardItem
        {
            Kind = ClipboardKind.Image,
            FilePath = @"C:\Temp\clip.png",
            ThumbPath = @"C:\Temp\clip-thumb.png",
            Mime = "image/png",
            Width = 640,
            Height = 480,
            ContentHash = "image-hash",
            SourceWindow = "截图工具",
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var saved = repository.Save(image, Now, ClipboardCapturePolicyWindow, 0);

        Assert.False(saved.Merged);
        Assert.Equal(ClipboardKind.Image, saved.Item.Kind);
        Assert.Equal(640, saved.Item.Width);
        Assert.Equal(480, saved.Item.Height);
        Assert.Equal("image/png", saved.Item.Mime);
        Assert.Equal(@"C:\Temp\clip-thumb.png", saved.Item.ThumbPath);
    }

    [Fact]
    public void Save_evicts_oldest_non_pinned_records_beyond_limit()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var evicted = 0;
        for (var index = 0; index < 5; index++)
        {
            var result = repository.Save(
                TextItem(index),
                Now.AddSeconds(index),
                ClipboardCapturePolicyWindow,
                retentionLimit: 3);
            evicted += result.Evicted.Count;
        }

        Assert.Equal(2, evicted);
        var recent = repository.QueryRecent(10);
        Assert.Equal(3, recent.Count);
        Assert.DoesNotContain(recent, item => item.NormalizedText == "内容-0");
        Assert.DoesNotContain(recent, item => item.NormalizedText == "内容-1");
        Assert.Equal("内容-4", recent[0].NormalizedText);
    }

    [Fact]
    public void Pinned_items_are_exempt_from_count_and_eviction()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var first = repository.Save(TextItem(0), Now, ClipboardCapturePolicyWindow, 0);
        Assert.True(repository.SetPinned(first.Item.Id, true, Now.AddSeconds(10)));

        for (var index = 1; index < 5; index++)
        {
            var result = repository.Save(
                TextItem(index),
                Now.AddSeconds(index),
                ClipboardCapturePolicyWindow,
                retentionLimit: 2);
            Assert.DoesNotContain(result.Evicted, item => item.Pinned);
        }

        var recent = repository.QueryRecent(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal(first.Item.Id, recent[0].Id);
        Assert.True(recent[0].Pinned);
        Assert.Equal(Now.AddSeconds(10), recent[0].PinnedAt);
    }

    [Fact]
    public void Pinned_items_sort_by_pinned_at_descending_and_unpin_restores_time_order()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var older = repository.Save(TextItem(0), Now, ClipboardCapturePolicyWindow, 0);
        var newer = repository.Save(TextItem(1), Now.AddSeconds(1), ClipboardCapturePolicyWindow, 0);

        repository.SetPinned(older.Item.Id, true, Now.AddSeconds(10));
        repository.SetPinned(newer.Item.Id, true, Now.AddSeconds(20));

        var pinnedOrder = repository.QueryRecent(10);
        Assert.Equal(newer.Item.Id, pinnedOrder[0].Id);
        Assert.Equal(older.Item.Id, pinnedOrder[1].Id);

        Assert.True(repository.SetPinned(newer.Item.Id, false, Now.AddSeconds(30)));
        var unpinnedOrder = repository.QueryRecent(10);
        Assert.Equal(older.Item.Id, unpinnedOrder[0].Id);
        Assert.Equal(Now.AddSeconds(10), unpinnedOrder[0].PinnedAt);
        Assert.Equal(newer.Item.Id, unpinnedOrder[1].Id);
        Assert.Null(unpinnedOrder[1].PinnedAt);
    }

    [Fact]
    public void Limit_zero_means_unlimited_and_evicts_nothing()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        for (var index = 0; index < 5; index++)
        {
            var result = repository.Save(
                TextItem(index),
                Now.AddSeconds(index),
                ClipboardCapturePolicyWindow,
                retentionLimit: 0);
            Assert.Empty(result.Evicted);
        }

        Assert.Equal(5, repository.QueryRecent(10).Count);
        Assert.Empty(repository.EvictOldestNonPinned(0));
        Assert.Equal(5, temp.CountRows("clipboard_item"));
    }

    [Fact]
    public void Soft_delete_hides_record_and_hard_delete_removes_row()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var saved = repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);

        Assert.True(repository.SoftDelete(saved.Item.Id, Now.AddMinutes(1)));
        Assert.Empty(repository.QueryRecent(10));
        Assert.Equal(1, temp.CountRows("clipboard_item"));
        Assert.False(repository.SetPinned(saved.Item.Id, true, Now));

        var deleted = repository.HardDelete(saved.Item.Id);
        Assert.NotNull(deleted);
        Assert.Equal(saved.Item.Id, deleted.Id);
        Assert.Equal(0, temp.CountRows("clipboard_item"));
        Assert.Null(repository.HardDelete(saved.Item.Id));
    }

    [Fact]
    public void Purge_removes_only_soft_deleted_records_older_than_cutoff()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var old = repository.Save(TextItem(0), Now, ClipboardCapturePolicyWindow, 0);
        var recent = repository.Save(TextItem(1), Now.AddDays(29), ClipboardCapturePolicyWindow, 0);
        repository.SoftDelete(old.Item.Id, Now);
        repository.SoftDelete(recent.Item.Id, Now.AddDays(35));

        Assert.Empty(repository.PurgeSoftDeletedBefore(Now.AddSeconds(-1)));

        var purged = repository.PurgeSoftDeletedBefore(Now.AddDays(30));
        Assert.Single(purged);
        Assert.Equal(old.Item.Id, purged[0].Id);
        Assert.Equal(1, temp.CountRows("clipboard_item"));
    }

    [Fact]
    public void Touch_moves_item_to_top_without_changing_duplicate_count()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var first = repository.Save(TextItem(0), Now, ClipboardCapturePolicyWindow, 0);
        var second = repository.Save(TextItem(1), Now.AddSeconds(10), ClipboardCapturePolicyWindow, 0);

        Assert.Equal(second.Item.Id, repository.QueryRecent(10)[0].Id);

        var touched = repository.Touch(first.Item.Id, Now.AddSeconds(20));

        Assert.NotNull(touched);
        Assert.Equal(Now.AddSeconds(20), touched.UpdatedAt);
        Assert.Equal(1, touched.DuplicateCount);
        Assert.Equal(first.Item.Id, repository.QueryRecent(10)[0].Id);
    }

    [Fact]
    public void Touch_returns_null_for_missing_or_soft_deleted_record()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var saved = repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);
        repository.SoftDelete(saved.Item.Id, Now.AddMinutes(1));

        Assert.Null(repository.Touch(saved.Item.Id, Now.AddMinutes(2)));
        Assert.Null(repository.Touch(long.MaxValue, Now));
    }

    [Fact]
    public void UpdateAnnotation_persists_note_and_tags_without_reordering()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var first = repository.Save(TextItem(0), Now, ClipboardCapturePolicyWindow, 0);
        var second = repository.Save(TextItem(1), Now.AddSeconds(10), ClipboardCapturePolicyWindow, 0);

        var updated = repository.UpdateAnnotation(
            first.Item.Id,
            "  会议纪要  ",
            ["工作", "重要"]);

        Assert.NotNull(updated);
        Assert.Equal("会议纪要", updated.Note);
        Assert.Equal(["工作", "重要"], updated.Tags);
        Assert.Equal(Now, updated.UpdatedAt);
        Assert.Equal(second.Item.Id, repository.QueryRecent(10)[0].Id);

        var cleared = repository.UpdateAnnotation(first.Item.Id, "   ", []);
        Assert.NotNull(cleared);
        Assert.Null(cleared.Note);
        Assert.Empty(cleared.Tags);
    }

    [Fact]
    public void UpdateAnnotation_returns_null_for_soft_deleted_record()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        var saved = repository.Save(TextItem(), Now, ClipboardCapturePolicyWindow, 0);
        repository.SoftDelete(saved.Item.Id, Now.AddMinutes(1));

        Assert.Null(repository.UpdateAnnotation(saved.Item.Id, "备注", ["标签"]));
    }

    private static TimeSpan ClipboardCapturePolicyWindow =>
        ClipboardCapturePolicy.DuplicateWindow;

    [Fact]
    public void Merge_different_sources_accumulates_sources_newest_first()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        repository.Save(TextItem() with { SourceWindow = "记事本" }, Now, ClipboardCapturePolicyWindow, 0);
        var merged = repository.Save(
            TextItem() with { SourceWindow = "Chrome · chrome.exe" },
            Now.AddSeconds(2),
            ClipboardCapturePolicyWindow,
            0,
            mergeDifferentSources: true);

        Assert.True(merged.Merged);
        Assert.Equal("Chrome · chrome.exe", merged.Item.SourceWindow);
        Assert.Equal(["Chrome · chrome.exe", "记事本"], merged.Item.Sources.ToArray());
        Assert.Single(repository.QueryRecent(10));
    }

    [Fact]
    public void Separate_sources_mode_only_merges_same_source()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new ClipboardRepository(temp.Database);

        repository.Save(
            TextItem() with { SourceWindow = "记事本" },
            Now,
            ClipboardCapturePolicyWindow,
            0,
            mergeDifferentSources: false);

        var other = repository.Save(
            TextItem() with { SourceWindow = "Chrome · chrome.exe" },
            Now.AddSeconds(2),
            ClipboardCapturePolicyWindow,
            0,
            mergeDifferentSources: false);
        Assert.False(other.Merged);
        Assert.Equal(2, temp.CountRows("clipboard_item"));

        var same = repository.Save(
            TextItem() with { SourceWindow = "记事本" },
            Now.AddSeconds(3),
            ClipboardCapturePolicyWindow,
            0,
            mergeDifferentSources: false);
        Assert.True(same.Merged);
        Assert.Equal(2, temp.CountRows("clipboard_item"));
    }

    private static ClipboardItem TextItem() => TextItem(0);

    private static ClipboardItem TextItem(int index) => new()
    {
        Kind = ClipboardKind.Text,
        TextContent = $"内容-{index}",
        NormalizedText = $"内容-{index}",
        Mime = "text/plain; charset=utf-8",
        ContentHash = $"hash-{index}",
        SourceWindow = "记事本",
        CreatedAt = Now.AddSeconds(index),
        UpdatedAt = Now.AddSeconds(index),
    };
}
