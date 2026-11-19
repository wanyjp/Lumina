using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// 贴图置顶窗口：无边框透明窗 + 阴影边框，可拖动、拖边缘/角等比缩放、滚轮缩放（以光标为锚点）、
/// 双击还原 100%、Esc 关闭。独立于主窗口，不阻塞任何流程。
/// </summary>
public partial class PinnedImageWindow : Window
{
    private const double MinScale = 0.15;
    private const double MaxScale = 10.0;

    /// <summary>阴影留白（Grid Margin 的一半），窗口尺寸 = 内容尺寸 + Frame × 2。</summary>
    private const double Frame = 14;

    /// <summary>边缘拖拽热区半径（含视觉边框内外两侧）。</summary>
    private const double GripSize = 7;

    private const double MinContentWidth = 60;
    private const double MinContentHeight = 40;

    private readonly double _baseWidth;
    private readonly double _baseHeight;

    private double _scale = 1.0;
    private bool _resizing;
    private ResizeEdge _resizeEdge = ResizeEdge.None;
    private Rect _resizeStartRect;
    private Point _resizeStartCursor;

    public PinnedImageWindow(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        InitializeComponent();

        using var stream = new MemoryStream(pngBytes);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Image.Source = image;

        var dpi = VisualTreeHelperDpi;
        _baseWidth = image.PixelWidth / dpi;
        _baseHeight = image.PixelHeight / dpi;
        Width = Math.Max(_baseWidth, MinContentWidth) + (Frame * 2);
        Height = Math.Max(_baseHeight, MinContentHeight) + (Frame * 2);

        MouseLeftButtonDown += OnDragMove;
        MouseDoubleClick += OnDoubleClick;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        MouseEnter += (_, _) => CloseButton.Opacity = 1;
        MouseLeave += (_, _) => CloseButton.Opacity = 0;
    }

    /// <summary>可拖拽的边缘 / 角。</summary>
    private enum ResizeEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private double VisualTreeHelperDpi
    {
        get
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget is not null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch (InvalidOperationException)
            {
                // 窗口未显示时按 96 DPI 处理。
            }

            return 1.0;
        }
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标未按下或窗口状态切换时忽略。
            }
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            return;
        }

        ApplyScale(1.0);
    }

    private void OnImageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        ApplyScale(_scale * factor, e.GetPosition(this));
    }

    /// <summary>悬停到边缘/角时切换成双向箭头，进入拖拽热区后按等比缩放窗口。</summary>
    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizing)
        {
            UpdateResize();
            e.Handled = true;
            return;
        }

        Cursor = CursorFor(GetEdge(e.GetPosition(this)));
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 关闭按钮优先，避免右上角被当成缩放热区。
        if (CloseButton.IsMouseOver)
        {
            return;
        }

        var edge = GetEdge(e.GetPosition(this));
        if (edge == ResizeEdge.None)
        {
            return;
        }

        _resizing = true;
        _resizeEdge = edge;
        _resizeStartRect = new Rect(Left, Top, Width, Height);
        _resizeStartCursor = GetCursorDip();
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        EndResize();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndResize();

    private void EndResize()
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        _resizeEdge = ResizeEdge.None;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <summary>按光标相对窗口的位置判断落在哪条边/哪个角（含边框内外一个热区半径）。</summary>
    private ResizeEdge GetEdge(Point point)
    {
        var left = Frame;
        var top = Frame;
        var right = Width - Frame;
        var bottom = Height - Frame;

        if (point.X < left - GripSize || point.X > right + GripSize
            || point.Y < top - GripSize || point.Y > bottom + GripSize)
        {
            return ResizeEdge.None;
        }

        var nearLeft = Math.Abs(point.X - left) <= GripSize;
        var nearRight = Math.Abs(point.X - right) <= GripSize;
        var nearTop = Math.Abs(point.Y - top) <= GripSize;
        var nearBottom = Math.Abs(point.Y - bottom) <= GripSize;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => ResizeEdge.TopLeft,
            (_, true, true, _) => ResizeEdge.TopRight,
            (true, _, _, true) => ResizeEdge.BottomLeft,
            (_, true, _, true) => ResizeEdge.BottomRight,
            (true, _, _, _) => ResizeEdge.Left,
            (_, true, _, _) => ResizeEdge.Right,
            (_, _, true, _) => ResizeEdge.Top,
            (_, _, _, true) => ResizeEdge.Bottom,
            _ => ResizeEdge.None,
        };
    }

    private static Cursor CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
        ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
        ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    private Point GetCursorDip()
    {
        var device = PointToScreen(Mouse.GetPosition(this));
        var scale = VisualTreeHelperDpi;
        return new Point(device.X / scale, device.Y / scale);
    }

    /// <summary>
    /// 拖拽缩放：始终按图片原始宽高比调整，边拖拽另一边自动跟随；
    /// 拖某条边时对边不动、垂直方向以中线为锚点，拖角时对角不动。
    /// </summary>
    private void UpdateResize()
    {
        var cursor = GetCursorDip();
        var aspect = _baseWidth / _baseHeight;
        var rect = ComputeResize(
            _resizeStartRect,
            _resizeEdge,
            cursor.X - _resizeStartCursor.X,
            cursor.Y - _resizeStartCursor.Y,
            aspect,
            _baseWidth);

        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
        _scale = (rect.Width - (Frame * 2)) / _baseWidth;
    }

    /// <summary>
    /// 纯几何计算：给定起始窗口矩形、拖拽边、光标位移与图片宽高比，返回新的窗口矩形。
    /// 抽成静态方法便于离线校验，不依赖真实鼠标。
    /// </summary>
    private static Rect ComputeResize(
        Rect start,
        ResizeEdge edge,
        double dx,
        double dy,
        double aspect,
        double baseWidth)
    {
        if (edge == ResizeEdge.None || start.Width <= 0 || start.Height <= 0
            || aspect <= 0 || baseWidth <= 0)
        {
            return start;
        }

        var startContentWidth = Math.Max(MinContentWidth, start.Width - (Frame * 2));
        var startContentHeight = Math.Max(MinContentHeight, start.Height - (Frame * 2));

        var isLeftEdge = edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
        var isRightEdge = edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight;
        var isTopEdge = edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;
        var isBottomEdge = edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;

        var signedDx = isLeftEdge ? -dx : isRightEdge ? dx : 0;
        var signedDy = isTopEdge ? -dy : isBottomEdge ? dy : 0;

        double contentWidth;
        if (edge is ResizeEdge.Left or ResizeEdge.Right)
        {
            contentWidth = startContentWidth + signedDx;
        }
        else if (edge is ResizeEdge.Top or ResizeEdge.Bottom)
        {
            contentWidth = (startContentHeight + signedDy) * aspect;
        }
        else
        {
            // 四角：取位移比例更大的方向驱动，避免斜向拖动时抖动。
            var byWidth = startContentWidth + signedDx;
            var byHeight = (startContentHeight + signedDy) * aspect;
            contentWidth = Math.Abs(signedDx) / startContentWidth >= Math.Abs(signedDy) / startContentHeight
                ? byWidth
                : byHeight;
        }

        var minWidth = Math.Max(MinContentWidth, MinContentHeight * aspect);
        contentWidth = Math.Clamp(contentWidth, minWidth, baseWidth * MaxScale);

        var contentHeight = contentWidth / aspect;
        var newWidth = contentWidth + (Frame * 2);
        var newHeight = contentHeight + (Frame * 2);

        var left = start.Left;
        var top = start.Top;

        if (isLeftEdge)
        {
            left = start.Right - newWidth;
        }
        else if (edge is ResizeEdge.Top or ResizeEdge.Bottom)
        {
            left = start.Left + ((start.Width - newWidth) / 2);
        }

        if (isTopEdge)
        {
            top = start.Bottom - newHeight;
        }
        else if (edge is ResizeEdge.Left or ResizeEdge.Right)
        {
            top = start.Top + ((start.Height - newHeight) / 2);
        }

        return new Rect(left, top, newWidth, newHeight);
    }

    private void ApplyScale(double target, Point? anchor = null)
    {
        var clamped = Math.Clamp(target, MinScale, MaxScale);
        if (Math.Abs(clamped - _scale) < 0.001)
        {
            return;
        }

        var ratio = clamped / _scale;
        var anchorPoint = anchor ?? new Point(Width / 2, Height / 2);

        // 以锚点为不动点缩放窗口矩形，实现“光标下的图像内容不动”。
        var newWidth = Math.Max(MinContentWidth, (Width - (Frame * 2)) * ratio) + (Frame * 2);
        var newHeight = Math.Max(MinContentHeight, (Height - (Frame * 2)) * ratio) + (Frame * 2);
        Left += anchorPoint.X * (1 - ratio);
        Top += anchorPoint.Y * (1 - ratio);
        Width = newWidth;
        Height = newHeight;
        _scale = clamped;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
