using Lumina.Domain.Colors;

namespace Lumina.Tests;

/// <summary>P2-05 取色像素提取：BGRA 顺序、边界钳制与 stride。</summary>
public class PixelSamplerTests
{
    private static readonly byte[] TwoByTwo =
    [
        10, 20, 30, 255,
        40, 50, 60, 255,
        70, 80, 90, 255,
        100, 110, 120, 255,
    ];

    [Fact]
    public void Samples_bgra_in_row_major_order()
    {
        Assert.Equal((30, 20, 10), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, 0, 0));
        Assert.Equal((60, 50, 40), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, 1, 0));
        Assert.Equal((90, 80, 70), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, 0, 1));
        Assert.Equal((120, 110, 100), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, 1, 1));
    }

    [Fact]
    public void Clamps_out_of_range_coordinates_to_edge_pixels()
    {
        Assert.Equal((30, 20, 10), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, -5, -5));
        Assert.Equal((120, 110, 100), PixelSampler.SampleBgra(TwoByTwo, 8, 2, 2, 99, 99));
    }

    [Fact]
    public void Honors_stride_and_rejects_invalid_buffers()
    {
        // 每行 2 像素 + 4 字节填充：stride = 12。
        var padded = new byte[24];
        padded[0] = 1;
        padded[1] = 2;
        padded[2] = 3;
        padded[4] = 4;
        padded[5] = 5;
        padded[6] = 6;
        padded[12] = 7;
        padded[13] = 8;
        padded[14] = 9;

        Assert.Equal((3, 2, 1), PixelSampler.SampleBgra(padded, 12, 2, 2, 0, 0));
        Assert.Equal((9, 8, 7), PixelSampler.SampleBgra(padded, 12, 2, 2, 0, 1));
        Assert.Equal((0, 0, 0), PixelSampler.SampleBgra(ReadOnlySpan<byte>.Empty, 12, 2, 2, 0, 0));
        Assert.Equal((0, 0, 0), PixelSampler.SampleBgra(padded, 4, 2, 2, 0, 0));
    }
}
