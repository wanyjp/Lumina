using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;

namespace Lumina.Tests;

/// <summary>P2-03 键盘路由与多选状态：固定项不可勾选、Esc 分层后退。</summary>
public class ClipboardKeyboardTests
{
    [Fact]
    public void Enter_delete_and_arrows_map_to_list_commands()
    {
        Assert.Equal(
            ClipboardCommand.ToggleDetail,
            ClipboardKeyboardRouter.Route(ClipboardKey.Enter, false, false, false, false));

        Assert.Equal(
            ClipboardCommand.DeleteCurrent,
            ClipboardKeyboardRouter.Route(ClipboardKey.Delete, false, false, false, false));

        Assert.Equal(
            ClipboardCommand.MoveUp,
            ClipboardKeyboardRouter.Route(ClipboardKey.Up, false, false, false, false));

        Assert.Equal(
            ClipboardCommand.MoveDown,
            ClipboardKeyboardRouter.Route(ClipboardKey.Down, false, false, false, false));

        Assert.Equal(
            ClipboardCommand.None,
            ClipboardKeyboardRouter.Route(ClipboardKey.None, false, false, false, false));
    }

    [Fact]
    public void Text_input_focus_suppresses_enter_delete_and_arrows()
    {
        foreach (var key in new[]
                 {
                     ClipboardKey.Enter, ClipboardKey.Delete, ClipboardKey.Up, ClipboardKey.Down,
                 })
        {
            Assert.Equal(
                ClipboardCommand.None,
                ClipboardKeyboardRouter.Route(key, textInputFocused: true, false, true, true));
        }
    }

    [Fact]
    public void Escape_backs_out_selection_then_search_then_detail()
    {
        Assert.Equal(
            ClipboardCommand.CancelSelection,
            ClipboardKeyboardRouter.Route(ClipboardKey.Escape, true, true, true, true));

        Assert.Equal(
            ClipboardCommand.ClearSearch,
            ClipboardKeyboardRouter.Route(ClipboardKey.Escape, true, false, true, true));

        Assert.Equal(
            ClipboardCommand.CollapseDetail,
            ClipboardKeyboardRouter.Route(ClipboardKey.Escape, false, false, false, true));

        Assert.Equal(
            ClipboardCommand.None,
            ClipboardKeyboardRouter.Route(ClipboardKey.Escape, false, false, false, false));
    }

    [Fact]
    public void Move_index_clamps_and_handles_empty_list()
    {
        Assert.Equal(-1, ClipboardKeyboardRouter.MoveIndex(-1, 1, 0));
        Assert.Equal(-1, ClipboardKeyboardRouter.MoveIndex(3, -1, 0));

        Assert.Equal(0, ClipboardKeyboardRouter.MoveIndex(-1, 1, 5));
        Assert.Equal(4, ClipboardKeyboardRouter.MoveIndex(-1, -1, 5));

        Assert.Equal(0, ClipboardKeyboardRouter.MoveIndex(0, -1, 5));
        Assert.Equal(4, ClipboardKeyboardRouter.MoveIndex(4, 1, 5));
        Assert.Equal(2, ClipboardKeyboardRouter.MoveIndex(1, 1, 5));
        Assert.Equal(1, ClipboardKeyboardRouter.MoveIndex(1, 0, 5));
    }

    [Fact]
    public void Selection_ignores_pinned_items()
    {
        var selection = new ClipboardSelection();
        var normal = Item(1, pinned: false);
        var pinned = Item(2, pinned: true);

        Assert.False(selection.Toggle(normal));
        Assert.Equal(0, selection.Count);

        selection.Begin();
        Assert.True(selection.Toggle(normal));
        Assert.False(selection.Toggle(pinned));
        Assert.Equal(1, selection.Count);
        Assert.True(selection.IsSelected(1));
        Assert.False(selection.IsSelected(2));

        Assert.False(selection.Toggle(normal));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Select_all_skips_pinned_and_reports_added_count()
    {
        var selection = new ClipboardSelection();
        var items = new[] { Item(1, false), Item(2, true), Item(3, false) };

        Assert.Equal(0, selection.SelectAll(items));

        selection.Begin();
        Assert.Equal(2, selection.SelectAll(items));
        Assert.Equal([1L, 3L], selection.Snapshot());

        // 重复全选不再新增。
        Assert.Equal(0, selection.SelectAll(items));
    }

    [Fact]
    public void Prune_drops_items_that_became_pinned_or_removed()
    {
        var selection = new ClipboardSelection();
        selection.Begin();
        selection.SelectAll([Item(1, false), Item(2, false), Item(3, false)]);
        Assert.Equal(3, selection.Count);

        selection.Prune([Item(1, false), Item(2, true)]);

        Assert.Equal([1L], selection.Snapshot());
    }

    [Fact]
    public void Cancel_and_clear_reset_selection_state()
    {
        var selection = new ClipboardSelection();
        selection.Begin();
        selection.Toggle(Item(1, false));

        selection.Clear();
        Assert.True(selection.IsActive);
        Assert.Equal(0, selection.Count);

        selection.Toggle(Item(1, false));
        selection.Cancel();
        Assert.False(selection.IsActive);
        Assert.Equal(0, selection.Count);
        Assert.Empty(selection.Snapshot());
    }

    private static ClipboardItem Item(long id, bool pinned) => new()
    {
        Id = id,
        Kind = ClipboardKind.Text,
        TextContent = $"内容-{id}",
        Pinned = pinned,
        CreatedAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8)),
        UpdatedAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(8)),
    };
}
