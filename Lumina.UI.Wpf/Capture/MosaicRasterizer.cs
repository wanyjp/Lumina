using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina.Domain.Capture;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// 马赛克栅格化：按块平均源图像素，生成缩小位图后交由
/// NearestNeighbor 拉伸得到方块效果。元素为不可变记录，
/// 以元素值作缓存键；元素集合变化时由调用方清空缓存。
/// </summary>
public sealed class MosaicRasterizer
{
    private readonly BitmapSource _source;
    private readonly Dictionary<AnnotationElement, BitmapSource?> _cache = [];

    public MosaicRasterizer(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public void Clear() => _cache.Clear();

    /// <summary>生成元素覆盖区域的马赛克位图；区域为空返回 null。</summary>
    public BitmapSource? Build(AnnotationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Kind != AnnotationKind.Mosaic)
        {
            return null;
        }

        if (_cache.TryGetValue(element, out var cached))
        {
            return cached;
        }

        var value = BuildCore(element);
        _cache[element] = value;
        return value;
    }

    private BitmapSource? BuildCore(AnnotationElement element)
    {
        var left = Math.Clamp((int)Math.Floor(element.X), 0, Math.Max(0, _source.PixelWidth - 1));
        var top = Math.Clamp((int)Math.Floor(element.Y), 0, Math.Max(0, _source.PixelHeight - 1));
        var right = Math.Clamp(
            (int)Math.Ceiling(element.X + element.Width),
            left + 1,
            _source.PixelWidth);
        var bottom = Math.Clamp(
            (int)Math.Ceiling(element.Y + element.Height),
            top + 1,
            _source.PixelHeight);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var block = MosaicPolicy.NormalizeBlockSize((int)Math.Round(element.Stroke));
        var columns = MosaicPolicy.BlockCount(width, block);
        var rows = MosaicPolicy.BlockCount(height, block);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        _source.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, 0);

        var averaged = new byte[columns * rows * 4];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var blockLeft = column * block;
                var blockTop = row * block;
                var blockRight = Math.Min(width, blockLeft + block);
                var blockBottom = Math.Min(height, blockTop + block);

                long blue = 0;
                long green = 0;
                long red = 0;
                long count = 0;

                for (var y = blockTop; y < blockBottom; y++)
                {
                    var offset = (y * stride) + (blockLeft * 4);
                    for (var x = blockLeft; x < blockRight; x++)
                    {
                        blue += pixels[offset];
                        green += pixels[offset + 1];
                        red += pixels[offset + 2];
                        count++;
                        offset += 4;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var target = ((row * columns) + column) * 4;
                averaged[target] = (byte)(blue / count);
                averaged[target + 1] = (byte)(green / count);
                averaged[target + 2] = (byte)(red / count);
                averaged[target + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(columns, rows, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, columns, rows), averaged, columns * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
