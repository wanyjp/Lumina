using Lumina.Data.Clipboard;
using Lumina.Data.Settings;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-03 看板编排：固定、删除（固定跳过）、备注标签与回贴前移。</summary>
public class ClipboardBoardServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Load_filters_by_tab_search_and_tag()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        repository.Save(Item(0, "会议纪要"), Now, TimeSpan.FromSeconds(5), 0);
        repository.Save(Item(1, "购物清单") with { Tags = ["生活"] }, Now.AddSeconds(10), TimeSpan.FromSeconds(5), 0);

        Assert.Equal(2, board.Load().Items.Count);
        Assert.Single(board.Load(search: "会议").Items);
        Assert.Single(board.Load(tag: "生活").Items);
        Assert.Empty(board.Load(search: "会议", tag: "生活").Items);
    }

    [Fact]
    public void SetPinned_marks_item_and_keeps_it_exempt_from_limit()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var saved = repository.Save(Item(0, "固定我"), Now, TimeSpan.FromSeconds(5), 0);

        Assert.True(board.SetPinned(saved.Item.Id, true));
        var pinned = Assert.Single(board.Load().Items);
        Assert.True(pinned.Pinned);
        Assert.NotNull(pinned.PinnedAt);

        Assert.True(board.SetPinned(saved.Item.Id, false));
        Assert.False(Assert.Single(board.Load().Items).Pinned);
    }

    [Fact]
    public void Delete_skips_pinned_items_and_soft_deletes_others()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var pinned = repository.Save(Item(0, "固定"), Now, TimeSpan.FromSeconds(5), 0);
        var normal = repository.Save(Item(1, "普通"), Now.AddSeconds(10), TimeSpan.FromSeconds(5), 0);
        board.SetPinned(pinned.Item.Id, true);

        var outcome = board.Delete([.. board.Load().Items]);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(1, outcome.SkippedPinned);

        var remaining = Assert.Single(repository.QueryRecent(10));
        Assert.Equal(pinned.Item.Id, remaining.Id);
        Assert.Equal(2, temp.CountRows("clipboard_item"));
        Assert.NotEqual(normal.Item.Id, remaining.Id);
    }

    [Fact]
    public void SaveAnnotation_trims_note_and_caps_tags()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var saved = repository.Save(Item(0, "正文"), Now, TimeSpan.FromSeconds(5), 0);

        var outcome = board.SaveAnnotation(
            saved.Item,
            "  重要备注  ",
            "一,二,三,四,五,六");

        Assert.True(outcome.Success);
        Assert.Equal(5, outcome.Tags.Count);
        Assert.Equal(1, outcome.Dropped);

        var reloaded = Assert.Single(repository.QueryRecent(10));
        Assert.Equal("重要备注", reloaded.Note);
        Assert.Equal(["一", "二", "三", "四", "五"], reloaded.Tags);
    }

    [Fact]
    public void SaveAnnotation_truncates_long_tag_to_twelve_chars()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var saved = repository.Save(Item(0, "正文"), Now, TimeSpan.FromSeconds(5), 0);

        var outcome = board.SaveAnnotation(saved.Item, null, "这是一个超过十二个字的标签名称");

        Assert.True(outcome.Success);
        var tag = Assert.Single(outcome.Tags);
        Assert.Equal(ClipboardTagPolicy.MaxLength, tag.Length);
    }

    [Fact]
    public void Copy_success_writes_clipboard_and_moves_item_to_top()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, writer) = Create(temp);

        var older = repository.Save(Item(0, "较早"), Now, TimeSpan.FromSeconds(5), 0);
        var newer = repository.Save(Item(1, "较新"), Now.AddSeconds(30), TimeSpan.FromSeconds(5), 0);

        Assert.Equal(newer.Item.Id, board.Load().Items[0].Id);

        var outcome = board.Copy(older.Item, plainText: false);

        Assert.True(outcome.Success);
        Assert.True(outcome.MovedToTop);
        Assert.Equal(older.Item.Id, board.Load().Items[0].Id);
        Assert.Equal("较早", writer.LastItem?.TextContent);
    }

    [Fact]
    public void Copy_failure_keeps_order_and_returns_manual_hint()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, writer) = Create(temp);
        writer.Result = false;

        var older = repository.Save(Item(0, "较早"), Now, TimeSpan.FromSeconds(5), 0);
        var newer = repository.Save(Item(1, "较新"), Now.AddSeconds(30), TimeSpan.FromSeconds(5), 0);

        var outcome = board.Copy(older.Item, plainText: false);

        Assert.False(outcome.Success);
        Assert.False(outcome.MovedToTop);
        Assert.Contains("Ctrl+C", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(newer.Item.Id, board.Load().Items[0].Id);
    }

    [Fact]
    public void Copy_plain_text_of_image_is_rejected_with_hint()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, _, writer) = Create(temp);

        var image = new ClipboardItem
        {
            Id = 1,
            Kind = ClipboardKind.Image,
            FilePath = "clipboard-images/a.png",
            Width = 240,
            Height = 160,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var outcome = board.Copy(image, plainText: true);

        Assert.False(outcome.Success);
        Assert.Contains("图片", outcome.Message, StringComparison.Ordinal);
        Assert.Null(writer.LastText);
    }

    [Fact]
    public void Copy_plain_text_of_html_uses_sanitized_body()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, _, writer) = Create(temp);

        var html = new ClipboardItem
        {
            Id = 1,
            Kind = ClipboardKind.Html,
            TextContent = "<p>正文<script>alert(1)</script></p>",
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var outcome = board.Copy(html, plainText: true);

        Assert.True(outcome.Success);
        Assert.Equal("正文", writer.LastText);
    }

    [Fact]
    public void SaveContent_reclassifies_between_text_and_link_and_recomputes_hash()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var link = repository.Save(
            Item(0, "https://example.com/a") with
            {
                Kind = ClipboardKind.Link,
                Url = "https://example.com/a",
                NormalizedText = null,
                Mime = "text/uri-list",
                ContentHash = "link-hash",
            },
            Now,
            TimeSpan.FromSeconds(5),
            0);

        var asText = board.SaveContent(link.Item, "普通文本内容");
        Assert.True(asText.Success);
        Assert.Equal(ClipboardKind.Text, asText.Kind);

        var textItem = Assert.Single(board.Load().Items);
        Assert.Equal(ClipboardKind.Text, textItem.Kind);
        Assert.Null(textItem.Url);
        Assert.Equal("普通文本内容", textItem.TextContent);
        Assert.NotNull(textItem.ContentHash);
        Assert.NotEqual("link-hash", textItem.ContentHash);

        var asLink = board.SaveContent(textItem, "https://example.com/b");
        Assert.True(asLink.Success);
        Assert.Equal(ClipboardKind.Link, asLink.Kind);

        var linkItem = Assert.Single(board.Load().Items);
        Assert.Equal("https://example.com/b", linkItem.Url);
        Assert.Equal("text/uri-list", linkItem.Mime);
    }

    [Fact]
    public void SaveContent_rejects_empty_and_image_items()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, repository, _) = Create(temp);

        var saved = repository.Save(Item(0, "文本"), Now, TimeSpan.FromSeconds(5), 0);
        Assert.False(board.SaveContent(saved.Item, "   ").Success);

        var image = repository.Save(
            new ClipboardItem
            {
                Kind = ClipboardKind.Image,
                FilePath = @"C:\Temp\a.png",
                ContentHash = "image-hash",
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            Now.AddSeconds(10),
            TimeSpan.FromSeconds(5),
            0);

        var outcome = board.SaveContent(image.Item, "任意文本");
        Assert.False(outcome.Success);
        Assert.Contains("图片与文件", outcome.Message);
    }

    [Fact]
    public void Tags_visible_follows_preference()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var (board, _, _) = Create(temp);

        Assert.True(board.TagsVisible);

        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        settings.Set(PreferenceKeys.ClipboardShowTags, false);

        var reloaded = new ClipboardBoardService(
            new ClipboardRepository(temp.Database),
            new ClipboardRetentionService(
                new ClipboardRepository(temp.Database),
                new SettingsStore(new SqliteSettingsBackend(temp.Database))),
            new StubWriter(),
            new SettingsStore(new SqliteSettingsBackend(temp.Database)));

        Assert.False(reloaded.TagsVisible);
    }

    private static (ClipboardBoardService Board, ClipboardRepository Repository, StubWriter Writer)
        Create(TemporaryDataRoot temp)
    {
        var repository = new ClipboardRepository(temp.Database);
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var retention = new ClipboardRetentionService(repository, settings);
        var writer = new StubWriter();
        return (new ClipboardBoardService(repository, retention, writer, settings), repository, writer);
    }

    private static ClipboardItem Item(int index, string text) => new()
    {
        Kind = ClipboardKind.Text,
        TextContent = text,
        NormalizedText = text,
        ContentHash = $"hash-{index}",
        SourceWindow = "记事本",
        CreatedAt = Now.AddSeconds(index),
        UpdatedAt = Now.AddSeconds(index),
    };

    private sealed class StubWriter : IClipboardWriter
    {
        public bool Result { get; set; } = true;

        public ClipboardItem? LastItem { get; private set; }

        public string? LastText { get; private set; }

        public bool TryWrite(ClipboardItem item)
        {
            LastItem = item;
            LastText = item.TextContent;
            return Result;
        }

        public bool TryWritePlainText(string text)
        {
            LastText = text;
            return Result;
        }
    }
}
