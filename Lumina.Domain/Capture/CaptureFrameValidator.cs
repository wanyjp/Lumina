namespace Lumina.Domain.Capture;

/// <summary>
/// 捕获帧有效性判定：识别 WGC 首帧空帧、PrintWindow 对 GPU/DirectComposition
/// 窗口返回的纯黑图，以及 alpha=0 的透明图。检测到黑帧后调用方应回落到下一级
/// 捕获方式（GitHub 社区共识做法：PrintWindow → WGC → 屏幕裁剪 + 黑帧检测）。
/// 判定为纯计算，不依赖图像库，便于自动回归。
/// </summary>
public static class CaptureFrameValidator
{
    /// <summary>网格采样边长；24×24 = 576 个采样点，足够覆盖纯色判断且开销可忽略。</summary>
    public const int SampleGrid = 24;

    /// <summary>非黑像素占比低于该阈值判为黑帧（0.5%）。</summary>
    public const double BlankRatioThreshold = 0.005;

    /// <summary>单像素"有内容"的通道阈值：alpha &gt; 8 且任一 RGB &gt; 12。</summary>
    public const byte ContentThreshold = 12;

    /// <summary>把 BGRA 缓冲的 alpha 全部置 255，避免 WGC 帧整体透明被当成黑图。</summary>
    public static void ForceOpaque(byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        for (var index = 3; index < bgra.Length; index += 4)
        {
            bgra[index] = 255;
        }
    }

    /// <summary>对自上而下、stride = width*4 的 BGRA 帧做网格采样判黑。</summary>
    public static bool IsBlankBgra(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        var stride = width * 4;
        if (width <= 0 || height <= 0 || bgra.Length < (long)stride * height)
        {
            return true;
        }

        var samples = 0;
        var nonBlank = 0;

        for (var row = 0; row < SampleGrid; row++)
        {
            var y = Math.Min(height - 1, (int)((long)(height - 1) * row / (SampleGrid - 1)));
            for (var column = 0; column < SampleGrid; column++)
            {
                var x = Math.Min(width - 1, (int)((long)(width - 1) * column / (SampleGrid - 1)));
                var offset = (y * stride) + (x * 4);

                samples++;
                if (bgra[offset + 3] > 8
                    && (bgra[offset + 2] > ContentThreshold
                        || bgra[offset + 1] > ContentThreshold
                        || bgra[offset] > ContentThreshold))
                {
                    nonBlank++;
                }
            }
        }

        return IsBlankSampleCount(samples, nonBlank);
    }

    /// <summary>按采样统计判黑；供 PNG 解码路径复用同一阈值。</summary>
    public static bool IsBlankSampleCount(int samples, int nonBlank)
    {
        if (samples <= 0)
        {
            return true;
        }

        if (nonBlank <= 0)
        {
            return true;
        }

        return (double)nonBlank / samples < BlankRatioThreshold;
    }

    /// <summary>生成网格采样坐标（行/列索引 → 像素坐标），PNG 与 BGRA 路径共用。</summary>
    public static int SampleCoordinate(int extent, int index)
    {
        if (extent <= 1)
        {
            return 0;
        }

        return Math.Min(extent - 1, (int)((long)(extent - 1) * index / (SampleGrid - 1)));
    }
}
