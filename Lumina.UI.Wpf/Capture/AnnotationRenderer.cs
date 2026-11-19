using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina.Domain.Capture;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// 标注绘制与导出：画布显示与最终栅格化共用同一套绘制逻辑，
/// 保证“所见即所得”。坐标一律为截图原图像素空间；导出按原图
/// 尺寸（96 DPI 等比）渲染，矢量元素与文字保持清晰。
/// </summary>
public static class AnnotationRenderer
{
    /// <summary>把标注元素栅格化到原图上并编码 PNG。</summary>
    public static byte[] ComposePng(
        BitmapSource source,
        IReadOnlyList<AnnotationElement> elements,
        MosaicRasterizer rasterizer)
    {
        var bitmap = ComposeBitmap(source, elements, rasterizer);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// 把标注元素栅格化到原图上，返回可直接用于裁剪/旋转/预览的冻结位图。
    /// 画布显示与最终导出共用同一套绘制逻辑，保证所见即所得。
    /// </summary>
    public static BitmapSource ComposeBitmap(
        BitmapSource source,
        IReadOnlyList<AnnotationElement> elements,
        MosaicRasterizer rasterizer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(rasterizer);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            foreach (var element in elements)
            {
                DrawElement(context, element, rasterizer.Build, pixelsPerDip: 1.0);
            }
        }

        var bitmap = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>在像素空间绘制单个元素（画布与导出共用）。</summary>
    public static void DrawElement(
        DrawingContext context,
        AnnotationElement element,
        Func<AnnotationElement, BitmapSource?> mosaicProvider,
        double pixelsPerDip)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mosaicProvider);

        switch (element.Kind)
        {
            case AnnotationKind.Rectangle:
                DrawRectangle(context, element);
                break;
            case AnnotationKind.Ellipse:
                DrawEllipse(context, element);
                break;
            case AnnotationKind.Arrow:
                DrawArrow(context, element);
                break;
            case AnnotationKind.Line:
                DrawLine(context, element);
                break;
            case AnnotationKind.Freehand:
                DrawFreehand(context, element);
                break;
            case AnnotationKind.Highlight:
                DrawHighlight(context, element);
                break;
            case AnnotationKind.Text:
                DrawText(context, element, pixelsPerDip);
                break;
            case AnnotationKind.Mosaic:
                DrawMosaic(context, element, mosaicProvider);
                break;
        }
    }

    private static void DrawEllipse(DrawingContext context, AnnotationElement element)
    {
        var pen = new Pen(CreateBrush(element.Color), Math.Max(1, element.Stroke));
        pen.Freeze();
        context.DrawEllipse(
            null,
            pen,
            new Point(element.X + (element.Width / 2), element.Y + (element.Height / 2)),
            element.Width / 2,
            element.Height / 2);
    }

    private static void DrawLine(DrawingContext context, AnnotationElement element)
    {
        var pen = new Pen(CreateBrush(element.Color), Math.Max(1, element.Stroke));
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.Freeze();
        context.DrawLine(pen, new Point(element.X, element.Y), new Point(element.X2, element.Y2));
    }

    private static void DrawHighlight(DrawingContext context, AnnotationElement element)
    {
        var brush = CreateBrush(AnnotationDefaults.WithAlpha(element.Color, AnnotationDefaults.HighlightAlpha));
        context.DrawRectangle(brush, null, new Rect(element.X, element.Y, element.Width, element.Height));
    }

    private static void DrawFreehand(DrawingContext context, AnnotationElement element)
    {
        if (element.Points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(
                new Point(element.Points[0].X, element.Points[0].Y),
                isFilled: false,
                isClosed: false);
            var segments = element.Points
                .Skip(1)
                .Select(point => new Point(point.X, point.Y))
                .ToList();
            geometryContext.PolyLineTo(segments, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        var pen = new Pen(CreateBrush(element.Color), Math.Max(1, element.Stroke))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawRectangle(DrawingContext context, AnnotationElement element)
    {
        var brush = CreateBrush(element.Color);
        var pen = new Pen(brush, Math.Max(1, element.Stroke));
        pen.Freeze();
        context.DrawRectangle(null, pen, new Rect(element.X, element.Y, element.Width, element.Height));
    }

    private static void DrawArrow(DrawingContext context, AnnotationElement element)
    {
        var brush = CreateBrush(element.Color);
        var pen = new Pen(brush, Math.Max(1, element.Stroke));
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.Freeze();

        var start = new Point(element.X, element.Y);
        var end = new Point(element.X2, element.Y2);
        context.DrawLine(pen, start, end);

        // 箭头头部：以终点为顶点的两段短翼，角度 ±26°。
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var headLength = Math.Max(10, element.Stroke * 3.5);
        var spread = 26.0 * Math.PI / 180.0;

        var wing1 = new Point(
            end.X - (headLength * Math.Cos(angle - spread)),
            end.Y - (headLength * Math.Sin(angle - spread)));
        var wing2 = new Point(
            end.X - (headLength * Math.Cos(angle + spread)),
            end.Y - (headLength * Math.Sin(angle + spread)));

        context.DrawLine(pen, end, wing1);
        context.DrawLine(pen, end, wing2);
    }

    private static void DrawText(DrawingContext context, AnnotationElement element, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(element.Text) || element.FontSize <= 0)
        {
            return;
        }

        var text = new FormattedText(
            element.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            element.FontSize,
            CreateBrush(element.Color),
            pixelsPerDip);

        // 文字元素加细描边底，保证浅色文字在浅色截图上可读。
        var outline = new GeometryDrawing
        {
            Geometry = text.BuildGeometry(new Point(element.X, element.Y)),
            Pen = new Pen(Brushes.Black, 0.8) { LineJoin = PenLineJoin.Round },
        };
        outline.Pen.Freeze();
        outline.Freeze();
        context.DrawDrawing(outline);
        context.DrawText(text, new Point(element.X, element.Y));
    }

    private static void DrawMosaic(
        DrawingContext context,
        AnnotationElement element,
        Func<AnnotationElement, BitmapSource?> mosaicProvider)
    {
        var bitmap = mosaicProvider(element);
        if (bitmap is null)
        {
            // 无法栅格化（区域为空）：退化为半透明遮块，不抛异常。
            var fallback = new SolidColorBrush(Color.FromArgb(120, 120, 120, 120));
            fallback.Freeze();
            context.DrawRectangle(
                fallback,
                null,
                new Rect(element.X, element.Y, element.Width, element.Height));
            return;
        }

        context.DrawImage(
            bitmap,
            new Rect(element.X, element.Y, element.Width, element.Height));
    }

    /// <summary>绘制选中态：虚线高亮框与尺寸手柄（仅画布使用）。</summary>
    public static void DrawSelectionChrome(
        DrawingContext context,
        AnnotationElement element,
        Brush accent,
        double handleSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accent);

        var pen = new Pen(accent, 1.2)
        {
            DashStyle = new DashStyle([3, 2], 0),
        };
        pen.Freeze();

        var handleFill = accent;
        var handleStroke = Brushes.White;

        if (element.Kind is AnnotationKind.Arrow or AnnotationKind.Line)
        {
            var bounds = new Rect(
                Math.Min(element.X, element.X2),
                Math.Min(element.Y, element.Y2),
                Math.Abs(element.X2 - element.X),
                Math.Abs(element.Y2 - element.Y));
            bounds.Inflate(4, 4);
            context.DrawRectangle(null, pen, bounds);
            DrawHandle(context, handleFill, handleStroke, handleSize, element.X, element.Y);
            DrawHandle(context, handleFill, handleStroke, handleSize, element.X2, element.Y2);
            return;
        }

        var rect = new Rect(element.X, element.Y, element.Width, element.Height);
        context.DrawRectangle(null, pen, rect);
        DrawHandle(context, handleFill, handleStroke, handleSize, rect.Left, rect.Top);
        DrawHandle(context, handleFill, handleStroke, handleSize, rect.Right, rect.Top);
        DrawHandle(context, handleFill, handleStroke, handleSize, rect.Left, rect.Bottom);
        DrawHandle(context, handleFill, handleStroke, handleSize, rect.Right, rect.Bottom);
    }

    private static void DrawHandle(
        DrawingContext context,
        Brush fill,
        Brush stroke,
        double size,
        double x,
        double y)
    {
        var half = size / 2;
        context.DrawRectangle(fill, new Pen(stroke, 1), new Rect(x - half, y - half, size, size));
    }

    private static SolidColorBrush CreateBrush(uint argb)
    {
        var color = Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>计算文字元素在给定字体参数下的边界（像素空间）。</summary>
    public static Size MeasureText(string text, double fontSize, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return new Size(formatted.Width, formatted.Height);
    }
}
