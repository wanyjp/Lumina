using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina.Domain.Capture;

namespace Lumina.UI.Wpf.Capture;

/// <summary>标注编辑工具。</summary>
public enum AnnotationTool
{
    Select = 0,
    Rectangle = 1,
    Arrow = 2,
    Text = 3,
    Mosaic = 4,
    Freehand = 5,
    Ellipse = 6,
    Line = 7,
    Highlight = 8,
    Crop = 9,
}

/// <summary>
/// 标注画布：自绘控件，承担元素渲染与鼠标交互（创建/选择/移动/缩放）。
/// 历史与元素集合归编辑器窗口持有，画布通过事件上报编辑结果；
/// 坐标一律换算为截图原图像素空间，与显示缩放无关。
/// </summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    private const double HandleDip = 10;
    private const double HitToleranceDip = 7;
    private const long MosaicPreviewIntervalMs = 60;

    private MosaicRasterizer _rasterizer;

    private BitmapSource? _source;
    private IReadOnlyList<AnnotationElement> _elements = [];
    private int _selectedIndex = -1;
    private AnnotationTool _tool = AnnotationTool.Select;
    private uint _color = AnnotationPalette.DefaultColor;
    private int _strokeLevel = 1;
    private double _fontSize = AnnotationDefaults.FontMedium;
    private long _lastDraftTick;

    private bool _creating;
    private Point _createStart;
    private AnnotationElement? _draft;
    private readonly List<AnnotationPoint> _freehandPoints = [];

    private Rect? _cropRect;
    private Point _cropStart;
    private bool _cropDragging;

    private bool _interacting;
    private Point _interactStart;
    private AnnotationElement? _interactOriginal;
    private AnnotationElement? _interactLive;
    private byte _interactHandle;

    /// <summary>新元素创建完成；由所有者提交历史并回填 <see cref="Elements"/>。</summary>
    public event EventHandler<AnnotationElement>? ElementCreated;

    /// <summary>元素移动/缩放/重编辑完成；参数为编辑后的新元素。</summary>
    public event EventHandler<AnnotationElement>? ElementEdited;

    /// <summary>选中项变化。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>文字工具落点（原图像素）；由所有者展示输入框。</summary>
    public event EventHandler<Point>? TextPlacementRequested;

    /// <summary>双击文字元素请求重新编辑。</summary>
    public event EventHandler<AnnotationElement>? TextEditRequested;

    /// <summary>裁剪选区变化（拖拽结束或清空时触发）。</summary>
    public event EventHandler? CropRectChanged;

    public AnnotationCanvas(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _rasterizer = new MosaicRasterizer(source);
        SnapsToDevicePixels = true;
        Focusable = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        Width = source.PixelWidth;
        Height = source.PixelHeight;
    }

    public BitmapSource? Source => _source;

    /// <summary>更换底图（裁剪/旋转/撤销后）；同时重置选择与裁剪选区。</summary>
    public void SetSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _rasterizer = new MosaicRasterizer(source);
        _selectedIndex = -1;
        _cropRect = null;
        _cropDragging = false;
        _elements = [];
        Width = source.PixelWidth;
        Height = source.PixelHeight;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CropRectChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>当前裁剪选区（原图像素空间）；未定义时返回 null。</summary>
    public Rect? CropRect => _cropRect;

    /// <summary>清除裁剪选区。</summary>
    public void ClearCropRect()
    {
        if (_cropRect is null)
        {
            return;
        }

        _cropRect = null;
        _cropDragging = false;
        InvalidateVisual();
        CropRectChanged?.Invoke(this, EventArgs.Empty);
    }

    public int ImageWidth => _source?.PixelWidth ?? 0;

    public int ImageHeight => _source?.PixelHeight ?? 0;

    public IReadOnlyList<AnnotationElement> Elements
    {
        get => _elements;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _elements = value;
            if (_selectedIndex >= _elements.Count)
            {
                _selectedIndex = -1;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            _rasterizer.Clear();
            InvalidateVisual();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = _elements.Count == 0 ? -1 : Math.Clamp(value, -1, _elements.Count - 1);
            if (_selectedIndex == clamped)
            {
                return;
            }

            _selectedIndex = clamped;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AnnotationElement? SelectedElement
        => _selectedIndex >= 0 && _selectedIndex < _elements.Count ? _elements[_selectedIndex] : null;

    public AnnotationTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == AnnotationTool.Crop && value != AnnotationTool.Crop && _cropRect is not null)
            {
                _cropRect = null;
                CropRectChanged?.Invoke(this, EventArgs.Empty);
            }

            _tool = value;
            CancelInteraction();
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public uint Color
    {
        get => _color;
        set
        {
            _color = value;
            InvalidateVisual();
        }
    }

    public int StrokeLevel
    {
        get => _strokeLevel;
        set
        {
            _strokeLevel = Math.Clamp(value, 0, 2);
            InvalidateVisual();
        }
    }

    /// <summary>文字字号（原图像素）；可用界面下拉自定义。</summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = AnnotationDefaults.NormalizeFontSize(value);
            InvalidateVisual();
        }
    }

    /// <summary>方向键微调当前选中元素；Shift 由调用方决定步长。</summary>
    public void NudgeSelected(double offsetX, double offsetY)
    {
        if (SelectedElement is not { } selected)
        {
            return;
        }

        ElementEdited?.Invoke(this, selected.WithOffset(offsetX, offsetY));
    }

    /// <summary>显示缩放：画布 DIP 尺寸与原图像素尺寸之比。</summary>
    public double Scale
    {
        get
        {
            if (_source is null || _source.PixelWidth == 0 || _source.PixelHeight == 0)
            {
                return 1;
            }

            var byWidth = ActualWidth > 0 ? ActualWidth / _source.PixelWidth : 1;
            var byHeight = ActualHeight > 0 ? ActualHeight / _source.PixelHeight : 1;
            return byWidth > 0 && byHeight > 0 ? Math.Min(byWidth, byHeight) : 1;
        }
    }

    /// <summary>中止进行中的创建/移动/缩放（Esc 或切换工具时调用）。</summary>
    public void CancelInteraction()
    {
        if (!_creating && !_interacting)
        {
            return;
        }

        _creating = false;
        _draft = null;
        _interacting = false;
        _interactOriginal = null;
        _interactLive = null;
        _interactHandle = 0;
        _cropDragging = false;
        InvalidateVisual();
    }

    private Point ClampToImage(Point point)
        => new(Math.Clamp(point.X, 0, ImageWidth), Math.Clamp(point.Y, 0, ImageHeight));

    private static Rect NormalizeRect(Point first, Point second)
    {
        var x = Math.Min(first.X, second.X);
        var y = Math.Min(first.Y, second.Y);
        return new Rect(
            x,
            y,
            Math.Max(0, Math.Max(first.X, second.X) - x),
            Math.Max(0, Math.Max(first.Y, second.Y) - y));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_source is null)
        {
            return;
        }

        var scale = Scale;
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        try
        {
            drawingContext.DrawImage(_source, new Rect(0, 0, ImageWidth, ImageHeight));

            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (var index = 0; index < _elements.Count; index++)
            {
                if (index == _selectedIndex && _interactLive is not null)
                {
                    continue;
                }

                AnnotationRenderer.DrawElement(drawingContext, _elements[index], _rasterizer.Build, pixelsPerDip);
            }

            if (_interactLive is not null)
            {
                DrawLive(drawingContext, _interactLive, pixelsPerDip);
            }

            if (_draft is not null)
            {
                DrawLive(drawingContext, _draft, pixelsPerDip);
            }

            var chromeTarget = _interactLive ?? SelectedElement;
            if (chromeTarget is not null)
            {
                AnnotationRenderer.DrawSelectionChrome(
                    drawingContext,
                    chromeTarget,
                    AccentBrush,
                    HandleDip / scale);
            }

            if (_tool == AnnotationTool.Crop && _cropRect is { } crop)
            {
                DrawCropOverlay(drawingContext, crop, scale);
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    /// <summary>裁剪模式：选区外压暗、选区内透亮，白框与四角手柄标示边界。</summary>
    private void DrawCropOverlay(DrawingContext context, Rect crop, double scale)
    {
        var dim = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0));
        dim.Freeze();

        context.DrawRectangle(dim, null, new Rect(0, 0, ImageWidth, Math.Max(0, crop.Top)));
        context.DrawRectangle(
            dim,
            null,
            new Rect(0, crop.Bottom, ImageWidth, Math.Max(0, ImageHeight - crop.Bottom)));
        context.DrawRectangle(
            dim,
            null,
            new Rect(0, crop.Top, Math.Max(0, crop.Left), crop.Height));
        context.DrawRectangle(
            dim,
            null,
            new Rect(crop.Right, crop.Top, Math.Max(0, ImageWidth - crop.Right), crop.Height));

        var white = new SolidColorBrush(Colors.White);
        white.Freeze();
        var dashed = new Pen(white, Math.Max(1, 1.5 / scale))
        {
            DashStyle = DashStyles.Dash,
        };
        dashed.Freeze();
        context.DrawRectangle(null, dashed, crop);

        var handle = HandleDip / scale;
        foreach (var corner in new[]
                 {
                     new Point(crop.Left, crop.Top),
                     new Point(crop.Right, crop.Top),
                     new Point(crop.Left, crop.Bottom),
                     new Point(crop.Right, crop.Bottom),
                 })
        {
            context.DrawRectangle(
                white,
                null,
                new Rect(corner.X - (handle / 2), corner.Y - (handle / 2), handle, handle));
        }
    }

    private static Brush AccentBrush
    {
        get
        {
            var brush = new SolidColorBrush(SystemColors.HighlightColor);
            brush.Freeze();
            return brush;
        }
    }

    private void DrawLive(DrawingContext context, AnnotationElement element, double pixelsPerDip)
    {
        // 拖拽中的马赛克也走真实栅格化（60ms 节流），所见即所得；
        // 区域为空时 DrawMosaic 内部退化为半透明占位。
        AnnotationRenderer.DrawElement(context, element, _rasterizer.Build, pixelsPerDip);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_source is null)
        {
            return;
        }

        var point = ToImagePoint(e.GetPosition(this));
        CaptureMouse();

        if (_tool == AnnotationTool.Crop)
        {
            _cropStart = ClampToImage(point);
            _cropRect = new Rect(_cropStart, new Size(0, 0));
            _cropDragging = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_tool == AnnotationTool.Select)
        {
            var doubleClickIndex = e.ClickCount == 2 ? HitElement(point) : -1;
            if (doubleClickIndex >= 0 && _elements[doubleClickIndex].Kind == AnnotationKind.Text)
            {
                ReleaseMouseCapture();
                TextEditRequested?.Invoke(this, _elements[doubleClickIndex]);
            }
            else
            {
                HandleSelectDown(point);
            }
        }
        else if (_tool == AnnotationTool.Text)
        {
            TextPlacementRequested?.Invoke(this, point);
        }
        else
        {
            _creating = true;
            _createStart = point;
            _draft = null;
            _freehandPoints.Clear();
            if (_tool == AnnotationTool.Freehand)
            {
                _freehandPoints.Add(new AnnotationPoint(point.X, point.Y));
            }

            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_source is null)
        {
            return;
        }

        var point = ToImagePoint(e.GetPosition(this));

        if (_cropDragging
            && _tool == AnnotationTool.Crop
            && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _cropRect = NormalizeRect(_cropStart, ClampToImage(point));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_creating)
        {
            if (_tool == AnnotationTool.Freehand)
            {
                AppendFreehandPoint(point);
            }
            else if (_tool == AnnotationTool.Mosaic
                     && Environment.TickCount64 - _lastDraftTick < MosaicPreviewIntervalMs)
            {
                // 马赛克实时预览逐帧重采样较重，60ms 节流（约 16fps）。
                return;
            }

            _lastDraftTick = Environment.TickCount64;
            _draft = BuildDraft(_createStart, point);
            InvalidateVisual();
        }
        else if (_interacting && _interactOriginal is { } original)
        {
            if (original.Kind == AnnotationKind.Mosaic
                && Environment.TickCount64 - _lastDraftTick < MosaicPreviewIntervalMs)
            {
                return;
            }

            _lastDraftTick = Environment.TickCount64;
            _interactLive = UpdateInteract(original, point);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void AppendFreehandPoint(Point point)
    {
        var candidate = new AnnotationPoint(point.X, point.Y);
        if (_freehandPoints.Count > 0)
        {
            var last = _freehandPoints[^1];
            var distance = Math.Sqrt(
                ((candidate.X - last.X) * (candidate.X - last.X))
                + ((candidate.Y - last.Y) * (candidate.Y - last.Y)));
            if (distance < AnnotationDefaults.FreehandSampleDistance / Scale)
            {
                return;
            }
        }

        _freehandPoints.Add(candidate);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_source is null)
        {
            return;
        }

        var point = ToImagePoint(e.GetPosition(this));
        ReleaseMouseCapture();

        if (_cropDragging)
        {
            _cropDragging = false;
            var rect = NormalizeRect(_cropStart, ClampToImage(point));
            _cropRect = rect.Width >= 8 && rect.Height >= 8 ? rect : null;
            InvalidateVisual();
            CropRectChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_creating)
        {
            _creating = false;
            var draft = BuildDraft(_createStart, point);
            _draft = null;
            if (draft is not null && IsValidDraft(draft))
            {
                ElementCreated?.Invoke(this, draft);
            }

            InvalidateVisual();
        }
        else if (_interacting)
        {
            var live = _interactLive;
            _interacting = false;
            _interactOriginal = null;
            _interactLive = null;
            _interactHandle = 0;
            if (live is not null)
            {
                ElementEdited?.Invoke(this, live);
            }

            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_tool == AnnotationTool.Crop)
        {
            ClearCropRect();
        }

        CancelInteraction();
        e.Handled = true;
    }

    private void HandleSelectDown(Point point)
    {
        if (SelectedElement is { } selected && HitHandle(selected, point, out var handle))
        {
            _interacting = true;
            _interactStart = point;
            _interactOriginal = selected;
            _interactHandle = handle;
            _interactLive = selected;
            return;
        }

        var hit = HitElement(point);
        if (hit >= 0)
        {
            SelectedIndex = hit;
            _interacting = true;
            _interactStart = point;
            _interactOriginal = _elements[hit];
            _interactHandle = 0;
            _interactLive = _elements[hit];
            return;
        }

        SelectedIndex = -1;
    }

    private AnnotationElement? BuildDraft(Point start, Point end)
    {
        var startPoint = new AnnotationPoint(start.X, start.Y);
        var constraint = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? _tool is AnnotationTool.Arrow or AnnotationTool.Line
                ? AnnotationDragConstraint.AxisAligned
                : AnnotationDragConstraint.Square
            : AnnotationDragConstraint.None;
        var constrained = AnnotationGeometry.ConstrainEnd(startPoint, new AnnotationPoint(end.X, end.Y), constraint);
        var constrainedEnd = new Point(constrained.X, constrained.Y);

        switch (_tool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
            case AnnotationTool.Highlight:
            case AnnotationTool.Mosaic:
                {
                    var bounds = AnnotationElement.NormalizeBounds(
                        start.X, start.Y, constrainedEnd.X, constrainedEnd.Y);
                    var kind = _tool switch
                    {
                        AnnotationTool.Rectangle => AnnotationKind.Rectangle,
                        AnnotationTool.Ellipse => AnnotationKind.Ellipse,
                        AnnotationTool.Highlight => AnnotationKind.Highlight,
                        _ => AnnotationKind.Mosaic,
                    };
                    return new AnnotationElement
                    {
                        Kind = kind,
                        X = bounds.X,
                        Y = bounds.Y,
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Color = _color,
                        Stroke = _tool == AnnotationTool.Mosaic
                            ? AnnotationDefaults.MosaicBlock(_strokeLevel)
                            : AnnotationDefaults.StrokeSize(_strokeLevel),
                    };
                }

            case AnnotationTool.Arrow:
            case AnnotationTool.Line:
                return new AnnotationElement
                {
                    Kind = _tool == AnnotationTool.Arrow ? AnnotationKind.Arrow : AnnotationKind.Line,
                    X = start.X,
                    Y = start.Y,
                    X2 = constrainedEnd.X,
                    Y2 = constrainedEnd.Y,
                    Color = _color,
                    Stroke = AnnotationDefaults.StrokeSize(_strokeLevel),
                };

            case AnnotationTool.Freehand:
                {
                    var points = _freehandPoints.ToList();
                    var last = points.Count > 0 ? points[^1] : startPoint;
                    var tip = new AnnotationPoint(constrainedEnd.X, constrainedEnd.Y);
                    if (points.Count == 0 || Math.Abs(last.X - tip.X) > 0.5 || Math.Abs(last.Y - tip.Y) > 0.5)
                    {
                        points.Add(tip);
                    }

                    if (points.Count < 2)
                    {
                        return null;
                    }

                    var bounds = AnnotationElement.BoundsOf(points);
                    return new AnnotationElement
                    {
                        Kind = AnnotationKind.Freehand,
                        X = bounds.X,
                        Y = bounds.Y,
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Points = points.ToArray(),
                        Color = _color,
                        Stroke = AnnotationDefaults.StrokeSize(_strokeLevel),
                    };
                }

            default:
                return null;
        }
    }

    private static bool IsValidDraft(AnnotationElement draft) => draft.Kind switch
    {
        AnnotationKind.Arrow or AnnotationKind.Line => Math.Sqrt(
            ((draft.X2 - draft.X) * (draft.X2 - draft.X))
            + ((draft.Y2 - draft.Y) * (draft.Y2 - draft.Y))) >= AnnotationDefaults.DragMinimum,
        AnnotationKind.Freehand => draft.Points.Count >= 2
            && Math.Max(draft.Width, draft.Height) >= AnnotationDefaults.DragMinimum,
        _ => draft.Width >= AnnotationDefaults.DragMinimum && draft.Height >= AnnotationDefaults.DragMinimum,
    };

    private int HitElement(Point point)
    {
        var tolerance = HitToleranceDip / Scale;
        for (var index = _elements.Count - 1; index >= 0; index--)
        {
            if (AnnotationGeometry.HitTest(_elements[index], point.X, point.Y, tolerance))
            {
                return index;
            }
        }

        return -1;
    }

    private bool HitHandle(AnnotationElement element, Point point, out byte handle)
    {
        handle = 0;
        var tolerance = HandleDip / Scale;

        if (element.Kind is AnnotationKind.Arrow or AnnotationKind.Line)
        {
            if (Near(point, element.X, element.Y, tolerance))
            {
                handle = 1;
                return true;
            }

            if (Near(point, element.X2, element.Y2, tolerance))
            {
                handle = 2;
                return true;
            }

            return false;
        }

        var left = element.X;
        var top = element.Y;
        var right = element.X + element.Width;
        var bottom = element.Y + element.Height;

        if (Near(point, left, top, tolerance))
        {
            handle = 1;
        }
        else if (Near(point, right, top, tolerance))
        {
            handle = 2;
        }
        else if (Near(point, left, bottom, tolerance))
        {
            handle = 3;
        }
        else if (Near(point, right, bottom, tolerance))
        {
            handle = 4;
        }

        return handle != 0;
    }

    private AnnotationElement UpdateInteract(AnnotationElement original, Point point)
    {
        var offsetX = point.X - _interactStart.X;
        var offsetY = point.Y - _interactStart.Y;

        AnnotationElement live;

        if (_interactHandle == 0)
        {
            live = original.WithOffset(offsetX, offsetY);
        }
        else if (original.Kind is AnnotationKind.Arrow or AnnotationKind.Line)
        {
            live = _interactHandle == 1
                ? original with { X = point.X, Y = point.Y }
                : original with { X2 = point.X, Y2 = point.Y };
        }
        else
        {
            var left = original.X;
            var top = original.Y;
            var right = original.X + original.Width;
            var bottom = original.Y + original.Height;

            switch (_interactHandle)
            {
                case 1:
                    left = point.X;
                    top = point.Y;
                    break;
                case 2:
                    right = point.X;
                    top = point.Y;
                    break;
                case 3:
                    left = point.X;
                    bottom = point.Y;
                    break;
                case 4:
                    right = point.X;
                    bottom = point.Y;
                    break;
            }

            var x = Math.Min(left, right);
            var y = Math.Min(top, bottom);
            var width = Math.Abs(right - left);
            var height = Math.Abs(bottom - top);

            if (original.Kind == AnnotationKind.Text)
            {
                // 文字按高度比例缩放字号，位置取新左上角。
                var ratio = original.Height > 0 ? height / original.Height : 1;
                var fontSize = Math.Clamp(original.FontSize * ratio, 8, 400);
                var measured = AnnotationRenderer.MeasureText(original.Text, fontSize, 1.0);
                live = original with
                {
                    X = x,
                    Y = y,
                    Width = measured.Width,
                    Height = measured.Height,
                    FontSize = fontSize,
                };
            }
            else if (original.Kind == AnnotationKind.Freehand)
            {
                var oldBounds = new AnnotationBounds(original.X, original.Y, original.Width, original.Height);
                var newBounds = new AnnotationBounds(x, y, width, height);
                live = original with
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Points = AnnotationGeometry.ScalePoints(original.Points, oldBounds, newBounds),
                };
            }
            else
            {
                live = original with { X = x, Y = y, Width = width, Height = height };
            }
        }

        return AnnotationGeometry.ClampToImage(live, ImageWidth, ImageHeight);
    }

    private Point ToImagePoint(Point displayPoint)
        => new(displayPoint.X / Scale, displayPoint.Y / Scale);

    private static bool Near(Point point, double x, double y, double tolerance)
        => Math.Abs(point.X - x) <= tolerance && Math.Abs(point.Y - y) <= tolerance;

    private void UpdateCursor()
    {
        Cursor = _tool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
    }
}
