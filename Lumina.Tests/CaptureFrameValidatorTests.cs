using Lumina.Domain.Capture;

namespace Lumina.Tests;

/// <summary>P2-04 反馈修复：捕获帧黑图检测与 alpha 归一化的自动回归。</summary>
public class CaptureFrameValidatorTests
{
    private const int Width = 240;
    private const int Height = 160;

    private static byte[] BlankFrame(byte alpha = 255)
    {
        var bytes = new byte[Width * Height * 4];
        if (alpha == 0)
        {
            return bytes;
        }

        for (var index = 3; index < bytes.Length; index += 4)
        {
            bytes[index] = alpha;
        }

        return bytes;
    }

    private static void SetBright(byte[] bytes, int sampleRow, int sampleColumn)
    {
        var x = CaptureFrameValidator.SampleCoordinate(Width, sampleColumn);
        var y = CaptureFrameValidator.SampleCoordinate(Height, sampleRow);
        var offset = ((y * Width) + x) * 4;
        bytes[offset] = 230;     // B
        bytes[offset + 1] = 230; // G
        bytes[offset + 2] = 230; // R
        bytes[offset + 3] = 255; // A
    }

    [Fact]
    public void Opaque_black_frame_is_blank()
    {
        Assert.True(CaptureFrameValidator.IsBlankBgra(BlankFrame(), Width, Height));
    }

    [Fact]
    public void Fully_transparent_frame_is_blank()
    {
        Assert.True(CaptureFrameValidator.IsBlankBgra(BlankFrame(alpha: 0), Width, Height));
    }

    [Fact]
    public void Near_black_dark_theme_pixels_stay_blank()
    {
        var bytes = BlankFrame();
        for (var index = 0; index < bytes.Length; index += 4)
        {
            bytes[index] = 8;
            bytes[index + 1] = 9;
            bytes[index + 2] = 10;
        }

        Assert.True(CaptureFrameValidator.IsBlankBgra(bytes, Width, Height));
    }

    [Fact]
    public void Bright_content_frame_is_not_blank()
    {
        var bytes = BlankFrame();
        for (var row = 0; row < CaptureFrameValidator.SampleGrid; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                SetBright(bytes, row, column);
            }
        }

        Assert.False(CaptureFrameValidator.IsBlankBgra(bytes, Width, Height));
    }

    [Fact]
    public void Single_bright_pixel_below_threshold_is_still_blank()
    {
        var bytes = BlankFrame();
        SetBright(bytes, 0, 0);

        Assert.True(CaptureFrameValidator.IsBlankBgra(bytes, Width, Height));
    }

    [Fact]
    public void ForceOpaque_sets_every_alpha_to_255()
    {
        var bytes = BlankFrame(alpha: 0);
        bytes[0] = 40;
        bytes[1] = 80;
        bytes[2] = 120;

        CaptureFrameValidator.ForceOpaque(bytes);

        Assert.All(Enumerable.Range(0, Width * Height), index => Assert.Equal(255, bytes[(index * 4) + 3]));
        Assert.Equal(40, bytes[0]);
        Assert.Equal(80, bytes[1]);
        Assert.Equal(120, bytes[2]);
    }

    [Fact]
    public void Invalid_buffer_is_blank()
    {
        Assert.True(CaptureFrameValidator.IsBlankBgra([], Width, Height));
        Assert.True(CaptureFrameValidator.IsBlankBgra(new byte[8], Width, Height));
        Assert.True(CaptureFrameValidator.IsBlankBgra(BlankFrame(), 0, 0));
    }

    [Fact]
    public void Blank_ratio_threshold_is_consistent()
    {
        var samples = CaptureFrameValidator.SampleGrid * CaptureFrameValidator.SampleGrid;

        Assert.True(CaptureFrameValidator.IsBlankSampleCount(samples, 0));
        Assert.True(CaptureFrameValidator.IsBlankSampleCount(samples, 1));
        Assert.False(CaptureFrameValidator.IsBlankSampleCount(samples, (int)(samples * 0.02)));
        Assert.True(CaptureFrameValidator.IsBlankSampleCount(0, 0));
    }

    [Fact]
    public void Sample_coordinates_stay_inside_bounds()
    {
        for (var index = 0; index < CaptureFrameValidator.SampleGrid; index++)
        {
            var x = CaptureFrameValidator.SampleCoordinate(Width, index);
            var y = CaptureFrameValidator.SampleCoordinate(Height, index);
            Assert.InRange(x, 0, Width - 1);
            Assert.InRange(y, 0, Height - 1);
        }

        Assert.Equal(0, CaptureFrameValidator.SampleCoordinate(1, 23));
    }
}
