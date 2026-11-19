using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lumina.SystemAdapters.Imaging;

/// <summary>
/// 截图缩略图：等比缩放到 <see cref="MaxWidth"/> 宽并以 PNG 输出；
/// 原图本身不超过阈值时直接返回原字节，避免无意义的二次编码。
/// </summary>
public static class ScreenshotThumbnailer
{
    public const int MaxWidth = 240;

    public static byte[] Create(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            return [];
        }

        using var input = new MemoryStream(pngBytes);
        using var source = Image.FromStream(input);
        if (source.Width <= MaxWidth)
        {
            return pngBytes;
        }

        var scale = (double)MaxWidth / source.Width;
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var thumbnail = new Bitmap(MaxWidth, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, MaxWidth, height);
        }

        using var output = new MemoryStream();
        thumbnail.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
