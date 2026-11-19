using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Domain.Capture;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// PNG 帧黑图检测：解码后按 24×24 网格采样，把统计结果交给
/// Domain 的 <see cref="CaptureFrameValidator"/> 判定。解码失败不判黑
/// （避免把可用结果误杀），由调用方继续处理。
/// </summary>
internal static class PngFrameInspector
{
    public static bool IsBlank(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            return true;
        }

        try
        {
            using var stream = new MemoryStream(pngBytes);
            using var bitmap = new Bitmap(stream);
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return true;
            }

            var samples = 0;
            var nonBlank = 0;
            for (var row = 0; row < CaptureFrameValidator.SampleGrid; row++)
            {
                var y = CaptureFrameValidator.SampleCoordinate(bitmap.Height, row);
                for (var column = 0; column < CaptureFrameValidator.SampleGrid; column++)
                {
                    var x = CaptureFrameValidator.SampleCoordinate(bitmap.Width, column);
                    var pixel = bitmap.GetPixel(x, y);

                    samples++;
                    if (pixel.A > 8
                        && (pixel.R > CaptureFrameValidator.ContentThreshold
                            || pixel.G > CaptureFrameValidator.ContentThreshold
                            || pixel.B > CaptureFrameValidator.ContentThreshold))
                    {
                        nonBlank++;
                    }
                }
            }

            return CaptureFrameValidator.IsBlankSampleCount(samples, nonBlank);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return false;
        }
    }
}
