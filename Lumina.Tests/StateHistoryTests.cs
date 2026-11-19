using Lumina.Domain.Capture;

namespace Lumina.Tests;

/// <summary>编辑器快照撤销栈：底图与标注编辑共用同一条撤销链的语义守卫。</summary>
public class StateHistoryTests
{
    private sealed record Snapshot(string Image, int Count);

    [Fact]
    public void Initial_state_is_current_and_nothing_to_undo()
    {
        var history = new StateHistory<Snapshot>(new Snapshot("origin", 0));

        Assert.Equal("origin", history.Current.Image);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void Commit_then_undo_then_redo_restores_image_and_elements()
    {
        var history = new StateHistory<Snapshot>(new Snapshot("origin", 0));
        history.Commit(new Snapshot("origin", 1));
        history.Commit(new Snapshot("cropped", 0));

        Assert.Equal("cropped", history.Current.Image);
        Assert.Equal("origin", history.Undo()!.Image);
        Assert.Equal(1, history.Current.Count);
        Assert.Equal("origin", history.Undo()!.Image);
        Assert.Equal(0, history.Current.Count);
        Assert.Null(history.Undo());

        Assert.Equal(1, history.Redo()!.Count);
        Assert.Equal("cropped", history.Redo()!.Image);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Commit_after_undo_clears_redo_stack()
    {
        var history = new StateHistory<Snapshot>(new Snapshot("origin", 0));
        history.Commit(new Snapshot("rotated", 0));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Commit(new Snapshot("cropped", 0));
        Assert.False(history.CanRedo);
        Assert.Equal("cropped", history.Current.Image);
    }

    [Fact]
    public void Capacity_drops_oldest_snapshot()
    {
        var history = new StateHistory<Snapshot>(new Snapshot("s0", 0), capacity: 3);
        for (var index = 1; index <= 5; index++)
        {
            history.Commit(new Snapshot($"s{index}", index));
        }

        var undoCount = 0;
        while (history.Undo() is not null)
        {
            undoCount++;
        }

        Assert.Equal(3, undoCount);
        Assert.Equal("s2", history.Current.Image);
    }

    [Fact]
    public void Constructor_rejects_null_initial_and_small_capacity()
    {
        Assert.Throws<ArgumentNullException>(() => new StateHistory<Snapshot>(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateHistory<Snapshot>(new Snapshot("x", 0), 1));
    }
}
