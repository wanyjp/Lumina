using Lumina.Services.Contracts;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 取色探针：全屏覆盖层放大镜拾取，颜色按 sRGB 直接读取。
/// 用户按 Esc / 右键取消时抛 <see cref="OperationCanceledException"/>，与截图流程保持一致。
/// </summary>
public sealed class ColorProbeService : IColorProbe
{
    public event Action<ColorSample>? Sampled;

    public ColorSample Pick()
    {
        var sample = ColorPickerOverlay.Run()
            ?? throw new OperationCanceledException("用户取消了取色。");

        Sampled?.Invoke(sample);
        return sample;
    }
}
