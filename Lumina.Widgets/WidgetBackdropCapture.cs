using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Lumina.Widgets;

/// <summary>屏幕像素坐标下的抓屏区域。</summary>
internal readonly record struct CaptureBounds(int Left, int Top, int Width, int Height);

/// <summary>
/// 组件玻璃层的背景采样器：抓屏、盒式降采样、提色，并判断画面是否变化。
/// 抓屏瞬间把自身窗口标记为“不参与捕获”，避免把组件拍进背景；
/// 降采样交给 GDI 的 HALFTONE 拉伸完成，只有 ~240px 的小图进入托管内存，
/// 因此 5–30fps 采样也不会产生大对象或明显 CPU 占用。
/// </summary>
internal sealed class WidgetBackdropSampler
{
    private const uint WdaNone = 0x0;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint SrcCopy = 0x00CC0020;
    private const int Halftone = 4;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    /// <summary>扩散后的目标宽度（像素）：越小越“液态”，但颜色层次会减少。</summary>
    private const int DiffuseWidth = 240;

    /// <summary>玻璃对背景的饱和度增益，接近系统材质对桌面色彩的“提色”效果。</summary>
    private const double Saturation = 1.18;

    private const int BrightnessLift = 4;

    /// <summary>单通道累计差值超过该值视为该像素发生了变化。</summary>
    private const int ChangeThreshold = 36;

    /// <summary>
    /// 变化像素占比达到该百分比才判定“画面在动”。取 3% 是为了忽略角落里跳秒的时钟、
    /// 闪烁的光标这类局部动画，避免它们把采样长期顶在 30fps。
    /// </summary>
    private const int ChangePercent = 3;

    private byte[] _stage = Array.Empty<byte>();
    private byte[] _pixels = Array.Empty<byte>();
    private int _frameCount;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo info,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        IntPtr source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr deviceContext, int x, int y, IntPtr previous);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>降采样后的 BGRA 像素，可直接写入 <c>WriteableBitmap</c>。</summary>
    public byte[] Pixels => _pixels;

    /// <summary>
    /// 计算卡片四周外扩 <paramref name="padding"/> DIP 的屏幕像素区域（须在 UI 线程调用）。
    /// </summary>
    public static CaptureBounds? Measure(Window window, FrameworkElement card, int padding = 42)
    {
        if (card.ActualWidth < 8 || card.ActualHeight < 8)
        {
            return null;
        }

        Point origin;
        try
        {
            origin = card.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(card);
        var width = (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY);
        var padX = (int)Math.Round(padding * dpi.DpiScaleX);
        var padY = (int)Math.Round(padding * dpi.DpiScaleY);

        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = GetSystemMetrics(SmCxVirtualScreen);
        var virtualHeight = GetSystemMetrics(SmCyVirtualScreen);
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return null;
        }

        var left = Math.Max(virtualLeft, (int)Math.Round(origin.X) - padX);
        var top = Math.Max(virtualTop, (int)Math.Round(origin.Y) - padY);
        var right = Math.Min(virtualLeft + virtualWidth, (int)Math.Round(origin.X) + width + padX);
        var bottom = Math.Min(virtualTop + virtualHeight, (int)Math.Round(origin.Y) + height + padY);
        var captureWidth = right - left;
        var captureHeight = bottom - top;
        if (captureWidth < 8 || captureHeight < 8)
        {
            return null;
        }

        return new CaptureBounds(left, top, captureWidth, captureHeight);
    }

    /// <summary>
    /// 抓取指定区域并生成小尺寸背景。可在后台线程调用。
    /// <paramref name="changed"/> 表示与上一帧相比画面是否明显变化。
    /// <paramref name="widthLimit"/> 与逐帧捕获共用同一套降采样比例，避免两条路径
    /// 产出不同尺寸的纹理；<paramref name="keepExcluded"/> 用于逐帧会话运行期间
    /// 保持“不参与捕获”，否则恢复 WDA_NONE 会让组件拍到自己。
    /// </summary>
    public bool TryCapture(
        IntPtr hwnd,
        CaptureBounds bounds,
        out int width,
        out int height,
        out bool changed,
        int widthLimit = DiffuseWidth,
        bool keepExcluded = false)
    {
        width = 0;
        height = 0;
        changed = false;
        if (hwnd == IntPtr.Zero || bounds.Width < 8 || bounds.Height < 8)
        {
            return false;
        }

        var factor = Math.Max(1, (int)Math.Round(bounds.Width / (double)Math.Max(48, widthLimit)));
        var targetWidth = Math.Max(4, bounds.Width / factor);
        var targetHeight = Math.Max(4, bounds.Height / factor);
        var bufferSize = targetWidth * targetHeight * 4;
        if (_stage.Length < bufferSize)
        {
            _stage = new byte[bufferSize];
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        var memory = IntPtr.Zero;
        var dib = IntPtr.Zero;
        var previous = IntPtr.Zero;
        var excluded = false;
        var captured = false;
        try
        {
            excluded = SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
            if (!excluded)
            {
                return false;
            }

            // 等待一次桌面合成，确保“排除自身”在抓屏前已生效。
            DwmFlush();

            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero)
            {
                return false;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = 40,
                    Width = targetWidth,
                    Height = -targetHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
            };
            dib = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return false;
            }

            previous = SelectObject(memory, dib);
            SetStretchBltMode(memory, Halftone);
            SetBrushOrgEx(memory, 0, 0, IntPtr.Zero);
            captured = StretchBlt(
                memory,
                0,
                0,
                targetWidth,
                targetHeight,
                screen,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                SrcCopy);
            if (!captured)
            {
                return false;
            }

            Marshal.Copy(bits, _stage, 0, bufferSize);
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero)
            {
                SelectObject(memory, previous);
            }

            if (dib != IntPtr.Zero)
            {
                DeleteObject(dib);
            }

            if (memory != IntPtr.Zero)
            {
                DeleteDC(memory);
            }

            ReleaseDC(IntPtr.Zero, screen);
            if (excluded)
            {
                // 逐帧会话运行时要保持排除，否则下一次捕获会把组件自己拍进去。
                SetWindowDisplayAffinity(hwnd, keepExcluded ? WdaExcludeFromCapture : WdaNone);
            }
        }

        if (!captured)
        {
            return false;
        }

        changed = Enrich(targetWidth, targetHeight, bufferSize);
        width = targetWidth;
        height = targetHeight;
        return true;
    }

    /// <summary>
    /// 对已经缩小到 ~240px 的画面做提色提亮，并与上一帧比对判断是否在动。
    /// 只有几万个像素，成本可以忽略。
    /// </summary>
    private bool Enrich(int width, int height, int size)
    {
        if (_pixels.Length != size)
        {
            _pixels = new byte[size];
            _frameCount = 0;
        }

        var differing = 0;
        var compared = 0;
        for (var index = 0; index < size; index += 4)
        {
            double blue = _stage[index];
            double green = _stage[index + 1];
            double red = _stage[index + 2];
            var gray = (0.299 * red) + (0.587 * green) + (0.114 * blue);
            var nextBlue = Clamp(blue, gray);
            var nextGreen = Clamp(green, gray);
            var nextRed = Clamp(red, gray);

            if (_frameCount > 0)
            {
                var delta = Math.Abs(nextBlue - _pixels[index])
                    + Math.Abs(nextGreen - _pixels[index + 1])
                    + Math.Abs(nextRed - _pixels[index + 2]);
                if (delta > ChangeThreshold)
                {
                    differing++;
                }

                compared++;
            }

            _pixels[index] = nextBlue;
            _pixels[index + 1] = nextGreen;
            _pixels[index + 2] = nextRed;
            _pixels[index + 3] = 0xFF;
        }

        _frameCount++;
        return compared > 0 && differing * 100 >= compared * ChangePercent;
    }

    private static byte Clamp(double value, double gray)
    {
        var boosted = gray + ((value - gray) * Saturation) + BrightnessLift;
        return (byte)Math.Clamp((int)Math.Round(boosted), 0, 255);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }
}
