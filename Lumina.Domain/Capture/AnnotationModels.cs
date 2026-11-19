namespace Lumina.Domain.Capture;

/// <summary>
/// 标注元素类型；数值为稳定键（导出参数与测试依赖），不得改动。
/// </summary>
public enum AnnotationKind
{
    Rectangle = 0,
    Arrow = 1,
    Text = 2,
    Mosaic = 3,

    // P2-04 反馈修复新增（纯追加，既有数值保持不变）。
    Freehand = 4,
    Ellipse = 5,
    Line = 6,
    Highlight = 7,
}

/// <summary>自由画笔轨迹点（原图像素空间），避免 Domain 依赖 WPF 类型。</summary>
public readonly record struct AnnotationPoint(double X, double Y);

/// <summary>拖拽约束：Shift 等比/正方形、直线轴向吸附。</summary>
public enum AnnotationDragConstraint
{
    None = 0,
    Square = 1,
    AxisAligned = 2,
}

/// <summary>
/// 矢量标注元素统一模型：矩形/文字/马赛克使用 X/Y/Width/Height 矩形边界；
/// 箭头使用 (X,Y)→(X2,Y2) 两点线段。坐标一律为截图原图像素空间，
/// 与显示缩放无关，导出时按原图尺寸栅格化。
/// </summary>
public sealed record AnnotationElement
{
    public required AnnotationKind Kind { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double X2 { get; init; }

    public double Y2 { get; init; }

    /// <summary>颜色，0xAARRGGBB；来自 <see cref="AnnotationPalette"/> 或界面拾取值。</summary>
    public uint Color { get; init; }

    /// <summary>矩形/箭头线宽；文字与马赛克不使用。</summary>
    public double Stroke { get; init; }

    /// <summary>文字内容；仅 <see cref="AnnotationKind.Text"/> 使用。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>文字字号（原图像素）；仅 <see cref="AnnotationKind.Text"/> 使用。</summary>
    public double FontSize { get; init; }

    /// <summary>自由画笔轨迹点；仅 <see cref="AnnotationKind.Freehand"/> 使用。</summary>
    public IReadOnlyList<AnnotationPoint> Points { get; init; } = [];

    /// <summary>整体平移（拖动移动元素）；箭头两端点与自由画笔点集同步平移。</summary>
    public AnnotationElement WithOffset(double offsetX, double offsetY) => this with
    {
        X = X + offsetX,
        Y = Y + offsetY,
        X2 = X2 + offsetX,
        Y2 = Y2 + offsetY,
        Points = Points.Count == 0
            ? Points
            : Points.Select(point => new AnnotationPoint(point.X + offsetX, point.Y + offsetY)).ToArray(),
    };

    /// <summary>由自由画笔点集计算包围盒。</summary>
    public static AnnotationBounds BoundsOf(IReadOnlyList<AnnotationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return new AnnotationBounds(0, 0, 0, 0);
        }

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return new AnnotationBounds(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>按两点拖拽结果构造矩形元素边界（自动归一化负向拖拽）。</summary>
    public static AnnotationBounds NormalizeBounds(
        double x1,
        double y1,
        double x2,
        double y2)
    {
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        return new AnnotationBounds(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

/// <summary>归一化矩形边界（原图像素空间）。</summary>
public readonly record struct AnnotationBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y)
        => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>按容差扩展边界后的包含判定（命中测试用）。</summary>
    public bool Contains(double x, double y, double tolerance)
        => x >= X - tolerance && x <= Right + tolerance && y >= Y - tolerance && y <= Bottom + tolerance;
}

/// <summary>标注默认参数；与界面三档控件一一对应。</summary>
public static class AnnotationDefaults
{
    public const double StrokeThin = 2;
    public const double StrokeMedium = 4;
    public const double StrokeThick = 6;

    public const double FontSmall = 20;
    public const double FontMedium = 28;
    public const double FontLarge = 40;

    public const int MosaicBlockSmall = 8;
    public const int MosaicBlockMedium = 16;
    public const int MosaicBlockLarge = 24;

    /// <summary>拖拽生成矩形/马赛克/箭头的最小像素；小于该值视为误触。</summary>
    public const double DragMinimum = 4;

    /// <summary>荧光高亮半透明填充的 alpha。</summary>
    public const byte HighlightAlpha = 96;

    /// <summary>自由画笔采样最小间距（像素），避免轨迹点过密。</summary>
    public const double FreehandSampleDistance = 2.5;

    /// <summary>可选字号档位（原图像素）。</summary>
    public static IReadOnlyList<double> FontSizes { get; } = [16, 20, 28, 40, 56];

    public const double FontSizeMinimum = 8;
    public const double FontSizeMaximum = 400;

    /// <summary>方向键微调步长；按住 Shift 放大 10 倍。</summary>
    public const double NudgeStep = 1;
    public const double NudgeLargeStep = 10;

    public static double NormalizeFontSize(double value)
        => Math.Clamp(value, FontSizeMinimum, FontSizeMaximum);

    /// <summary>从 0xAARRGGBB 生成带指定 alpha 的颜色。</summary>
    public static uint WithAlpha(uint argb, byte alpha)
        => (argb & 0x00FFFFFFu) | ((uint)alpha << 24);

    public static double StrokeSize(int level) => level switch
    {
        <= 0 => StrokeThin,
        1 => StrokeMedium,
        _ => StrokeThick,
    };

    public static double FontSize(int level) => level switch
    {
        <= 0 => FontSmall,
        1 => FontMedium,
        _ => FontLarge,
    };

    public static int MosaicBlock(int level) => level switch
    {
        <= 0 => MosaicBlockSmall,
        1 => MosaicBlockMedium,
        _ => MosaicBlockLarge,
    };
}

/// <summary>标注调色板；浅色与深色截图都可读的高饱和标记色。</summary>
public static class AnnotationPalette
{
    public static IReadOnlyList<(string Name, uint Argb)> All { get; } = new[]
    {
        ("红", 0xFFE84040u),
        ("橙", 0xFFF5871Fu),
        ("黄", 0xFFF5D400u),
        ("绿", 0xFF2FBF71u),
        ("蓝", 0xFF3E8EDEu),
        ("紫", 0xFF9B59D0u),
        ("白", 0xFFFFFFFFu),
        ("黑", 0xFF1A1A1Au),
    };

    public const uint DefaultColor = 0xFFE84040u;
}

/// <summary>标注几何：命中测试、边界钳制与线段距离；全部为纯函数。</summary>
public static class AnnotationGeometry
{
    /// <summary>元素命中测试；tolerance 为原图像素容差（界面按显示缩放换算后传入）。</summary>
    public static bool HitTest(AnnotationElement element, double x, double y, double tolerance)
    {
        switch (element.Kind)
        {
            case AnnotationKind.Arrow:
            case AnnotationKind.Line:
                {
                    var reach = tolerance + (element.Stroke / 2);
                    return DistanceToSegment(x, y, element.X, element.Y, element.X2, element.Y2) <= reach;
                }

            case AnnotationKind.Freehand:
                {
                    var reach = tolerance + (element.Stroke / 2);
                    return DistanceToPolyline(element.Points, x, y) <= reach;
                }

            case AnnotationKind.Ellipse:
                {
                    var radiusX = (element.Width / 2) + tolerance;
                    var radiusY = (element.Height / 2) + tolerance;
                    if (radiusX <= 0 || radiusY <= 0)
                    {
                        return false;
                    }

                    var centerX = element.X + (element.Width / 2);
                    var centerY = element.Y + (element.Height / 2);
                    var dx = (x - centerX) / radiusX;
                    var dy = (y - centerY) / radiusY;
                    return (dx * dx) + (dy * dy) <= 1;
                }

            default:
                {
                    var padding = element.Kind == AnnotationKind.Text ? 0 : tolerance;
                    return x >= element.X - padding
                        && x <= element.X + element.Width + padding
                        && y >= element.Y - padding
                        && y <= element.Y + element.Height + padding;
                }
        }
    }

    /// <summary>点到折线的最短距离；点集为空时返回极大值。</summary>
    public static double DistanceToPolyline(IReadOnlyList<AnnotationPoint> points, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return double.MaxValue;
        }

        if (points.Count == 1)
        {
            return Math.Sqrt(
                ((x - points[0].X) * (x - points[0].X))
                + ((y - points[0].Y) * (y - points[0].Y)));
        }

        var best = double.MaxValue;
        for (var index = 1; index < points.Count; index++)
        {
            var distance = DistanceToSegment(
                x, y,
                points[index - 1].X, points[index - 1].Y,
                points[index].X, points[index].Y);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Shift 拖拽约束：等比（正方形/正圆）或轴向吸附（水平/垂直/45°）。
    /// 保持起点不变，返回约束后的终点。
    /// </summary>
    public static AnnotationPoint ConstrainEnd(
        AnnotationPoint start,
        AnnotationPoint end,
        AnnotationDragConstraint constraint)
    {
        if (constraint == AnnotationDragConstraint.None)
        {
            return end;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        if (constraint == AnnotationDragConstraint.Square)
        {
            var extent = Math.Min(Math.Abs(dx), Math.Abs(dy));
            return new AnnotationPoint(
                start.X + (Math.Sign(dx) * extent),
                start.Y + (Math.Sign(dy) * extent));
        }

        // 轴向吸附：按主方向贴到水平、垂直或 45°。
        var length = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (Math.Abs(dx) > Math.Abs(dy) * 2)
        {
            return new AnnotationPoint(end.X, start.Y);
        }

        if (Math.Abs(dy) > Math.Abs(dx) * 2)
        {
            return new AnnotationPoint(start.X, end.Y);
        }

        return new AnnotationPoint(
            start.X + (Math.Sign(dx) * length),
            start.Y + (Math.Sign(dy) * length));
    }

    /// <summary>按新旧包围盒缩放自由画笔点集。</summary>
    public static IReadOnlyList<AnnotationPoint> ScalePoints(
        IReadOnlyList<AnnotationPoint> points,
        AnnotationBounds oldBounds,
        AnnotationBounds newBounds)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return points;
        }

        var scaleX = oldBounds.Width > 0 ? newBounds.Width / oldBounds.Width : 1;
        var scaleY = oldBounds.Height > 0 ? newBounds.Height / oldBounds.Height : 1;

        return points
            .Select(point => new AnnotationPoint(
                newBounds.X + ((point.X - oldBounds.X) * scaleX),
                newBounds.Y + ((point.Y - oldBounds.Y) * scaleY)))
            .ToArray();
    }

    /// <summary>点到线段距离（原图像素）。</summary>
    public static double DistanceToSegment(
        double px,
        double py,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared < double.Epsilon)
        {
            return Math.Sqrt(((px - x1) * (px - x1)) + ((py - y1) * (py - y1)));
        }

        var t = (((px - x1) * dx) + ((py - y1) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var cx = x1 + (t * dx);
        var cy = y1 + (t * dy);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }

    /// <summary>把元素边界钳制进原图范围；返回钳制后的新元素。</summary>
    public static AnnotationElement ClampToImage(AnnotationElement element, double imageWidth, double imageHeight)
    {
        if (element.Kind is AnnotationKind.Arrow or AnnotationKind.Line)
        {
            var x1 = Math.Clamp(element.X, 0, imageWidth);
            var y1 = Math.Clamp(element.Y, 0, imageHeight);
            var x2 = Math.Clamp(element.X2, 0, imageWidth);
            var y2 = Math.Clamp(element.Y2, 0, imageHeight);
            return element with { X = x1, Y = y1, X2 = x2, Y2 = y2 };
        }

        if (element.Kind == AnnotationKind.Freehand)
        {
            var clamped = element.Points
                .Select(point => new AnnotationPoint(
                    Math.Clamp(point.X, 0, imageWidth),
                    Math.Clamp(point.Y, 0, imageHeight)))
                .ToArray();
            var bounds = AnnotationElement.BoundsOf(clamped);
            return element with
            {
                Points = clamped,
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
            };
        }

        var width = Math.Min(element.Width, imageWidth);
        var height = Math.Min(element.Height, imageHeight);
        var x = Math.Clamp(element.X, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(element.Y, 0, Math.Max(0, imageHeight - height));
        return element with { X = x, Y = y, Width = width, Height = height };
    }
}

/// <summary>
/// 对象级撤销/重做栈：快照式保存每次编辑后的元素列表。
/// 提交新编辑会清空重做栈；超过容量的最旧快照被淘汰。
/// </summary>
public sealed class AnnotationHistory
{
    private readonly List<IReadOnlyList<AnnotationElement>> _undo = [];
    private readonly List<IReadOnlyList<AnnotationElement>> _redo = [];
    private IReadOnlyList<AnnotationElement> _current = [];

    public AnnotationHistory(int capacity = 100)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "撤销容量至少为 2。");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<AnnotationElement> Current => _current;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>提交一次编辑结果；重做栈清空，超容量淘汰最旧快照。</summary>
    public void Commit(IReadOnlyList<AnnotationElement> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        _undo.Add(_current);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _current = next;
    }

    /// <summary>撤销；无可撤销时返回 null 且状态不变。</summary>
    public IReadOnlyList<AnnotationElement>? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_current);
        _current = previous;
        return _current;
    }

    /// <summary>重做；无可重做时返回 null 且状态不变。</summary>
    public IReadOnlyList<AnnotationElement>? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_current);
        _current = next;
        return _current;
    }
}

/// <summary>马赛克策略：块大小钳制与网格划分。</summary>
public static class MosaicPolicy
{
    public const int BlockMinimum = 4;
    public const int BlockMaximum = 64;

    /// <summary>钳制块大小到 [4, 64]；非正值回落默认 16。</summary>
    public static int NormalizeBlockSize(int blockSize)
    {
        if (blockSize <= 0)
        {
            return AnnotationDefaults.MosaicBlockMedium;
        }

        return Math.Clamp(blockSize, BlockMinimum, BlockMaximum);
    }

    /// <summary>覆盖长度所需的块数（向上取整）。</summary>
    public static int BlockCount(int length, int blockSize)
    {
        var block = NormalizeBlockSize(blockSize);
        if (length <= 0)
        {
            return 0;
        }

        return (length + block - 1) / block;
    }

    /// <summary>枚举区域内的网格块（按块大小对齐区域原点）。</summary>
    public static IEnumerable<AnnotationBounds> Grid(AnnotationBounds bounds, int blockSize)
    {
        var block = NormalizeBlockSize(blockSize);
        var columns = BlockCount((int)Math.Ceiling(bounds.Width), block);
        var rows = BlockCount((int)Math.Ceiling(bounds.Height), block);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = bounds.X + (column * block);
                var y = bounds.Y + (row * block);
                var width = Math.Min(block, bounds.Right - x);
                var height = Math.Min(block, bounds.Bottom - y);
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                yield return new AnnotationBounds(x, y, width, height);
            }
        }
    }
}
