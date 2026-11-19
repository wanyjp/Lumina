namespace Lumina.Services.Widgets;

/// <summary>与 WPF 解耦的工作区矩形（DIP），便于在服务层单测边界收敛逻辑。</summary>
public readonly record struct PlacementArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// 小组件位置收敛：显示器拔插、分辨率/缩放变化、跨屏拖动后把窗口拉回可见区域。
/// 负坐标（副屏在主屏左侧/上方）是合法输入，必须原样支持。
/// </summary>
public static class WidgetPlacement
{
    public static (double Left, double Top) Clamp(
        double left,
        double top,
        double width,
        double height,
        PlacementArea workArea)
    {
        var maxLeft = Math.Max(workArea.Left, workArea.Right - Math.Max(width, 0));
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - Math.Max(height, 0));

        return (
            Math.Clamp(left, workArea.Left, maxLeft),
            Math.Clamp(top, workArea.Top, maxTop));
    }
}
