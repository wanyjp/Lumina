using Lumina.Data.Notes;
using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Services.Notes;

namespace Lumina.Tests;

/// <summary>
/// P3-05 便签：新增补时间戳、更新保留创建时间、置顶排序、
/// 颜色/标题/尺寸归一与 Changed 通知。
/// </summary>
public class NoteServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(8));

    private static NoteService CreateService(TemporaryDataRoot temp) =>
        new(new NoteRepository(temp.Database), () => Now);

    private static NoteService CreateService(TemporaryDataRoot temp, Func<DateTimeOffset> clock) =>
        new(new NoteRepository(temp.Database), clock);

    [Fact]
    public async Task Save_new_note_assigns_id_and_timestamps()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var saved = await service.SaveAsync(new NoteItem
        {
            Title = "  周会要点  ",
            Content = "第一行\r\n第二行",
            Color = "#E4868C",
            CreatedAt = Now,
            UpdatedAt = Now,
        }, TestContext.Current.CancellationToken);

        Assert.True(saved.Id > 0);
        Assert.Equal("周会要点", saved.Title);
        Assert.Equal("第一行\n第二行", saved.Content);
        Assert.Equal("rose", saved.Color);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task Update_keeps_created_at_and_raises_changed()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var clock = Now;
        var service = CreateService(temp, () => clock);
        var changes = 0;
        service.Changed += () => changes++;

        var created = await service.SaveAsync(new NoteItem
        {
            Content = "初始",
            CreatedAt = Now,
            UpdatedAt = Now,
        }, TestContext.Current.CancellationToken);

        var later = Now.AddHours(2);
        clock = later;
        var updated = await service.SaveAsync(created with
        {
            Content = "改过",
            Title = "标题",
            Pinned = true,
        }, TestContext.Current.CancellationToken);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(Now, updated.CreatedAt);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.True(updated.Pinned);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Query_all_puts_pinned_first_then_newest()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var repository = new NoteRepository(temp.Database);
        var service = new NoteService(repository, () => Now);

        var first = await service.SaveAsync(NewNote("A"), TestContext.Current.CancellationToken);
        var second = await service.SaveAsync(NewNote("B"), TestContext.Current.CancellationToken);
        var pinned = await service.SaveAsync(
            NewNote("C") with { Pinned = true },
            TestContext.Current.CancellationToken);

        var all = await service.AllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { pinned.Id, second.Id, first.Id }, all.Select(note => note.Id));
    }

    [Fact]
    public async Task Delete_removes_rows_and_raises_changed()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);
        var changes = 0;
        service.Changed += () => changes++;

        var note = await service.SaveAsync(NewNote("删除我"), TestContext.Current.CancellationToken);
        await service.DeleteAsync([note.Id], TestContext.Current.CancellationToken);

        Assert.Empty(await service.AllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Legacy_id_survives_update_and_size_is_clamped()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.SaveAsync(
            NewNote("旧数据") with
            {
                LegacyId = "note_1",
                Width = 10,
                Height = 5000,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(160d, created.Width);
        Assert.Equal(1600d, created.Height);

        var updated = await service.SaveAsync(
            created with { Content = "改过" },
            TestContext.Current.CancellationToken);
        Assert.Equal("note_1", updated.LegacyId);
    }

    [Fact]
    public async Task Blank_title_becomes_null_and_unknown_color_falls_back()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var saved = await service.SaveAsync(
            NewNote("   ") with { Color = "chartreuse" },
            TestContext.Current.CancellationToken);

        Assert.Null(saved.Title);
        Assert.Equal(NotePalette.DefaultKey, saved.Color);
    }

    private static NoteItem NewNote(string content) => new()
    {
        Content = content,
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
