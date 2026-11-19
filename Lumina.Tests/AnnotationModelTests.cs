using Lumina.Domain.Capture;

namespace Lumina.Tests;

/// <summary>
/// P2-04 标注模型守卫：稳定键、默认参数、几何命中与钳制、
/// 对象级撤销重做栈、马赛克块策略。
/// </summary>
public class AnnotationModelTests
{
    private static AnnotationElement Rectangle(
        double x = 10,
        double y = 20,
        double width = 100,
        double height = 50,
        uint color = 0xFFE84040u,
        double stroke = 4)
        => new()
        {
            Kind = AnnotationKind.Rectangle,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Color = color,
            Stroke = stroke,
        };

    [Fact]
    public void Annotation_kind_stable_keys_are_frozen()
    {
        Assert.Equal(0, (int)AnnotationKind.Rectangle);
        Assert.Equal(1, (int)AnnotationKind.Arrow);
        Assert.Equal(2, (int)AnnotationKind.Text);
        Assert.Equal(3, (int)AnnotationKind.Mosaic);
    }

    [Fact]
    public void Defaults_map_levels_to_sizes()
    {
        Assert.Equal(AnnotationDefaults.StrokeThin, AnnotationDefaults.StrokeSize(0));
        Assert.Equal(AnnotationDefaults.StrokeMedium, AnnotationDefaults.StrokeSize(1));
        Assert.Equal(AnnotationDefaults.StrokeThick, AnnotationDefaults.StrokeSize(2));
        Assert.Equal(AnnotationDefaults.StrokeThin, AnnotationDefaults.StrokeSize(-1));
        Assert.Equal(AnnotationDefaults.StrokeThick, AnnotationDefaults.StrokeSize(99));

        Assert.Equal(AnnotationDefaults.FontSmall, AnnotationDefaults.FontSize(0));
        Assert.Equal(AnnotationDefaults.FontMedium, AnnotationDefaults.FontSize(1));
        Assert.Equal(AnnotationDefaults.FontLarge, AnnotationDefaults.FontSize(2));

        Assert.Equal(AnnotationDefaults.MosaicBlockSmall, AnnotationDefaults.MosaicBlock(0));
        Assert.Equal(AnnotationDefaults.MosaicBlockMedium, AnnotationDefaults.MosaicBlock(1));
        Assert.Equal(AnnotationDefaults.MosaicBlockLarge, AnnotationDefaults.MosaicBlock(2));
    }

    [Fact]
    public void Palette_contains_readable_defaults()
    {
        Assert.Equal(8, AnnotationPalette.All.Count);
        Assert.Equal(AnnotationPalette.DefaultColor, AnnotationPalette.All[0].Argb);
        Assert.All(AnnotationPalette.All, entry => Assert.NotEqual(0u, (entry.Argb >> 24) & 0xFF));
    }

    [Fact]
    public void NormalizeBounds_handles_negative_drag()
    {
        var bounds = AnnotationElement.NormalizeBounds(120, 90, 30, 40);

        Assert.Equal(30, bounds.X);
        Assert.Equal(40, bounds.Y);
        Assert.Equal(90, bounds.Width);
        Assert.Equal(50, bounds.Height);
    }

    [Fact]
    public void HitTest_rectangle_uses_padded_bounds()
    {
        var element = Rectangle();

        Assert.True(AnnotationGeometry.HitTest(element, 60, 45, 2));
        Assert.True(AnnotationGeometry.HitTest(element, 8, 18, 3));
        Assert.False(AnnotationGeometry.HitTest(element, 4, 12, 3));
    }

    [Fact]
    public void HitTest_arrow_uses_segment_distance()
    {
        var arrow = new AnnotationElement
        {
            Kind = AnnotationKind.Arrow,
            X = 0,
            Y = 0,
            X2 = 100,
            Y2 = 0,
            Stroke = 4,
            Color = 0xFFE84040u,
        };

        Assert.True(AnnotationGeometry.HitTest(arrow, 50, 0, 3));
        Assert.True(AnnotationGeometry.HitTest(arrow, 50, 5, 3));
        Assert.False(AnnotationGeometry.HitTest(arrow, 50, 12, 3));
        // 端点外沿线不命中。
        Assert.False(AnnotationGeometry.HitTest(arrow, 130, 0, 3));
    }

    [Fact]
    public void DistanceTo_segment_degenerate_and_midpoint()
    {
        // 退化为点：距离即到该点的距离。
        Assert.Equal(5, AnnotationGeometry.DistanceToSegment(3, 9, 3, 4, 3, 4), 6);
        Assert.Equal(0, AnnotationGeometry.DistanceToSegment(3, 4, 3, 4, 3, 4), 6);
        Assert.Equal(0, AnnotationGeometry.DistanceToSegment(50, 0, 0, 0, 100, 0), 6);
        Assert.Equal(40, AnnotationGeometry.DistanceToSegment(50, 40, 0, 0, 100, 0), 6);
    }

    [Fact]
    public void ClampToImage_clamps_rect_and_arrow()
    {
        var rect = Rectangle(x: -20, y: 2600, width: 4000, height: 100);
        var clamped = AnnotationGeometry.ClampToImage(rect, 2560, 1600);

        Assert.Equal(0, clamped.X);
        Assert.Equal(1500, clamped.Y);
        Assert.Equal(2560, clamped.Width);
        Assert.Equal(100, clamped.Height);

        var arrow = new AnnotationElement
        {
            Kind = AnnotationKind.Arrow,
            X = -10,
            Y = 10,
            X2 = 3000,
            Y2 = 2000,
            Color = 0xFFE84040u,
            Stroke = 4,
        };
        var clampedArrow = AnnotationGeometry.ClampToImage(arrow, 2560, 1600);

        Assert.Equal(0, clampedArrow.X);
        Assert.Equal(10, clampedArrow.Y);
        Assert.Equal(2560, clampedArrow.X2);
        Assert.Equal(1600, clampedArrow.Y2);
    }

    [Fact]
    public void WithOffset_moves_rect_and_both_arrow_points()
    {
        var rect = Rectangle().WithOffset(15, -5);

        Assert.Equal(25, rect.X);
        Assert.Equal(15, rect.Y);
        Assert.Equal(100, rect.Width);

        var arrow = new AnnotationElement
        {
            Kind = AnnotationKind.Arrow,
            X = 10,
            Y = 10,
            X2 = 40,
            Y2 = 50,
            Color = 0xFFE84040u,
            Stroke = 4,
        }.WithOffset(5, 5);

        Assert.Equal(15, arrow.X);
        Assert.Equal(15, arrow.Y);
        Assert.Equal(45, arrow.X2);
        Assert.Equal(55, arrow.Y2);
    }

    [Fact]
    public void History_undo_redo_roundtrip()
    {
        var history = new AnnotationHistory();
        var first = Rectangle();
        var second = Rectangle(x: 1);

        history.Commit([first]);
        history.Commit([first, second]);

        Assert.Equal(2, history.Current.Count);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        var afterUndo = history.Undo();
        Assert.NotNull(afterUndo);
        Assert.Single(afterUndo);
        Assert.True(history.CanRedo);

        var afterRedo = history.Redo();
        Assert.NotNull(afterRedo);
        Assert.Equal(2, afterRedo.Count);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_commit_clears_redo_stack()
    {
        var history = new AnnotationHistory();
        history.Commit([Rectangle()]);
        history.Commit([Rectangle(), Rectangle(x: 5)]);

        Assert.NotNull(history.Undo());

        history.Commit([Rectangle(x: 9)]);

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo());
        Assert.Single(history.Current);
    }

    [Fact]
    public void History_undo_and_redo_return_null_when_empty()
    {
        var history = new AnnotationHistory();

        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
        Assert.Empty(history.Current);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_trims_oldest_snapshots_beyond_capacity()
    {
        var history = new AnnotationHistory(capacity: 3);

        for (var index = 0; index < 6; index++)
        {
            history.Commit([Rectangle(x: index)]);
        }

        var undoCount = 0;
        while (history.Undo() is not null)
        {
            undoCount++;
        }

        // 容量 3：可撤销步数为 3，最早一次编辑被淘汰，无法回到空态。
        Assert.Equal(3, undoCount);
        Assert.Single(history.Current);
    }

    [Fact]
    public void History_rejects_invalid_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationHistory(capacity: 1));
    }

    [Fact]
    public void MosaicPolicy_normalizes_block_size()
    {
        Assert.Equal(16, MosaicPolicy.NormalizeBlockSize(0));
        Assert.Equal(4, MosaicPolicy.NormalizeBlockSize(2));
        Assert.Equal(64, MosaicPolicy.NormalizeBlockSize(100));
        Assert.Equal(16, MosaicPolicy.NormalizeBlockSize(16));
    }

    [Fact]
    public void MosaicPolicy_block_count_rounds_up()
    {
        Assert.Equal(0, MosaicPolicy.BlockCount(0, 16));
        Assert.Equal(1, MosaicPolicy.BlockCount(16, 16));
        Assert.Equal(2, MosaicPolicy.BlockCount(17, 16));
        Assert.Equal(4, MosaicPolicy.BlockCount(64, 16));
    }

    [Fact]
    public void MosaicPolicy_grid_covers_region_without_overflow()
    {
        var blocks = MosaicPolicy.Grid(new AnnotationBounds(10, 20, 40, 33), 16).ToList();

        // 40 = 16+16+8 → 3 列；33 = 16+16+1 → 3 行。
        Assert.Equal(9, blocks.Count);
        Assert.All(blocks, block => Assert.True(block.Right <= 50));
        Assert.All(blocks, block => Assert.True(block.Bottom <= 53));
        Assert.Contains(blocks, block => block.Width == 8); // 40 = 16 + 16 + 8
        Assert.Contains(blocks, block => block.Height == 1); // 33 = 16 + 16 + 1
    }

    [Fact]
    public void New_annotation_kind_stable_keys_are_additive()
    {
        Assert.Equal(4, (int)AnnotationKind.Freehand);
        Assert.Equal(5, (int)AnnotationKind.Ellipse);
        Assert.Equal(6, (int)AnnotationKind.Line);
        Assert.Equal(7, (int)AnnotationKind.Highlight);
    }

    [Fact]
    public void ConstrainEnd_square_and_axis_aligned()
    {
        var start = new AnnotationPoint(10, 10);

        Assert.Equal(new AnnotationPoint(40, 30),
            AnnotationGeometry.ConstrainEnd(start, new AnnotationPoint(40, 30), AnnotationDragConstraint.None));
        Assert.Equal(new AnnotationPoint(30, 30),
            AnnotationGeometry.ConstrainEnd(start, new AnnotationPoint(40, 30), AnnotationDragConstraint.Square));
        Assert.Equal(new AnnotationPoint(-30, -30),
            AnnotationGeometry.ConstrainEnd(start, new AnnotationPoint(-40, -30), AnnotationDragConstraint.Square));

        Assert.Equal(new AnnotationPoint(50, 10),
            AnnotationGeometry.ConstrainEnd(start, new AnnotationPoint(50, 14), AnnotationDragConstraint.AxisAligned));
        Assert.Equal(new AnnotationPoint(10, 60),
            AnnotationGeometry.ConstrainEnd(start, new AnnotationPoint(14, 60), AnnotationDragConstraint.AxisAligned));
    }

    [Fact]
    public void DistanceToPolyline_handles_empty_and_segments()
    {
        Assert.Equal(double.MaxValue, AnnotationGeometry.DistanceToPolyline([], 5, 5));
        Assert.Equal(5, AnnotationGeometry.DistanceToPolyline([new AnnotationPoint(0, 0)], 3, 4), 6);
        Assert.Equal(0,
            AnnotationGeometry.DistanceToPolyline(
                [new AnnotationPoint(0, 0), new AnnotationPoint(100, 0)], 50, 0), 6);
        Assert.Equal(10,
            AnnotationGeometry.DistanceToPolyline(
                [new AnnotationPoint(0, 0), new AnnotationPoint(100, 0)], 50, 10), 6);
    }

    [Fact]
    public void BoundsOf_and_ScalePoints_roundtrip()
    {
        var points = new[]
        {
            new AnnotationPoint(10, 20),
            new AnnotationPoint(30, 60),
            new AnnotationPoint(20, 40),
        };
        var bounds = AnnotationElement.BoundsOf(points);

        Assert.Equal(10, bounds.X);
        Assert.Equal(20, bounds.Y);
        Assert.Equal(20, bounds.Width);
        Assert.Equal(40, bounds.Height);

        var scaled = AnnotationGeometry.ScalePoints(
            points,
            bounds,
            new AnnotationBounds(0, 0, 40, 80));

        Assert.Equal(new AnnotationPoint(0, 0), scaled[0]);
        Assert.Equal(new AnnotationPoint(40, 80), scaled[1]);
        Assert.Equal(new AnnotationPoint(20, 40), scaled[2]);
    }

    [Fact]
    public void WithOffset_moves_freehand_points()
    {
        var element = new AnnotationElement
        {
            Kind = AnnotationKind.Freehand,
            X = 10,
            Y = 20,
            Width = 30,
            Height = 40,
            Color = 0xFFE84040u,
            Stroke = 2,
            Points = [new AnnotationPoint(10, 20), new AnnotationPoint(40, 60)],
        };

        var moved = element.WithOffset(5, -5);

        Assert.Equal(15, moved.X);
        Assert.Equal(15, moved.Y);
        Assert.Equal(new AnnotationPoint(15, 15), moved.Points[0]);
        Assert.Equal(new AnnotationPoint(45, 55), moved.Points[1]);
    }

    [Fact]
    public void HitTest_covers_new_element_kinds()
    {
        var line = new AnnotationElement
        {
            Kind = AnnotationKind.Line,
            X = 0,
            Y = 0,
            X2 = 100,
            Y2 = 0,
            Color = 0xFFE84040u,
            Stroke = 4,
        };
        Assert.True(AnnotationGeometry.HitTest(line, 50, 0, 3));
        Assert.False(AnnotationGeometry.HitTest(line, 50, 20, 3));

        var ellipse = new AnnotationElement
        {
            Kind = AnnotationKind.Ellipse,
            X = 0,
            Y = 0,
            Width = 100,
            Height = 100,
            Color = 0xFFE84040u,
            Stroke = 4,
        };
        Assert.True(AnnotationGeometry.HitTest(ellipse, 50, 50, 2));
        Assert.False(AnnotationGeometry.HitTest(ellipse, 0, 0, 2));

        var highlight = new AnnotationElement
        {
            Kind = AnnotationKind.Highlight,
            X = 10,
            Y = 10,
            Width = 50,
            Height = 20,
            Color = 0xFFE84040u,
            Stroke = 4,
        };
        Assert.True(AnnotationGeometry.HitTest(highlight, 30, 20, 2));

        var freehand = new AnnotationElement
        {
            Kind = AnnotationKind.Freehand,
            X = 0,
            Y = 0,
            Width = 100,
            Height = 0,
            Color = 0xFFE84040u,
            Stroke = 4,
            Points = [new AnnotationPoint(0, 0), new AnnotationPoint(100, 0)],
        };
        Assert.True(AnnotationGeometry.HitTest(freehand, 50, 2, 3));
        Assert.False(AnnotationGeometry.HitTest(freehand, 50, 30, 3));
    }

    [Fact]
    public void Defaults_normalize_font_size_and_alpha()
    {
        Assert.Equal(AnnotationDefaults.FontSizeMinimum, AnnotationDefaults.NormalizeFontSize(1));
        Assert.Equal(AnnotationDefaults.FontSizeMaximum, AnnotationDefaults.NormalizeFontSize(9999));
        Assert.Equal(28, AnnotationDefaults.NormalizeFontSize(28));

        Assert.Equal(0x60E84040u, AnnotationDefaults.WithAlpha(0xFFE84040u, 0x60));
        Assert.Equal(5, AnnotationDefaults.FontSizes.Count);
    }
}
