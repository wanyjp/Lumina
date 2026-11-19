namespace Lumina.Domain.Colors;

/// <summary>
/// 取色像素提取：从 BGRA 缓冲区读取指定坐标的 sRGB 分量。
/// 越界按边缘钳制（放大镜靠近屏幕边缘时不出现黑边），stride 允许大于 width × 4。
/// </summary>
public static class PixelSampler
{
    public static (byte R, byte G, byte B) SampleBgra(
        ReadOnlySpan<byte> pixels,
        int stride,
        int width,
        int height,
        int x,
        int y)
    {
        if (pixels.IsEmpty || width <= 0 || height <= 0 || stride < width * 4)
        {
            return (0, 0, 0);
        }

        var px = Math.Clamp(x, 0, width - 1);
        var py = Math.Clamp(y, 0, height - 1);
        var offset = py * stride + px * 4;
        if (offset < 0 || offset + 3 >= pixels.Length)
        {
            return (0, 0, 0);
        }

        return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
