using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// GDI 屏幕捕获：区域 BitBlt（含分层窗口）与窗口 PrintWindow 回落。
/// 全部按物理像素操作；失败返回 null，由调用方继续降级或抛出可读异常。
/// </summary>
internal static class GdiCapture
{
    /// <summary>把 BGRA（自上而下、stride = width*4）编码为 PNG 字节。</summary>
    public static byte[] EncodeBgraToPng(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        var stride = width * 4;
        if (width <= 0 || height <= 0 || bgra.Length < stride * height)
        {
            throw new ArgumentOutOfRangeException(nameof(bgra), "BGRA 缓冲与尺寸不匹配。");
        }

        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(
                width,
                height,
                stride,
                PixelFormat.Format32bppArgb,
                handle.AddrOfPinnedObject());
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// 捕获虚拟屏上的任意矩形（物理像素，可为负坐标）。
    /// SRCCOPY | CAPTUREBLT 保证分层窗口（托盘提示、桌面小工具）一并入图。
    /// </summary>
    public static byte[] CaptureScreenRegionToPng(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new IOException("截图区域为空，无法生成图像。");
        }

        var screenDc = ScreenNativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new IOException("无法获取屏幕设备上下文，截图失败。");
        }

        try
        {
            var memoryDc = ScreenNativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new IOException("无法创建兼容设备上下文，截图失败。");
            }

            try
            {
                var bitmap = ScreenNativeMethods.CreateCompatibleBitmap(screenDc, width, height);
                if (bitmap == IntPtr.Zero)
                {
                    throw new IOException("无法创建兼容位图，截图失败。");
                }

                try
                {
                    var previous = ScreenNativeMethods.SelectObject(memoryDc, bitmap);
                    try
                    {
                        if (!ScreenNativeMethods.BitBlt(
                                memoryDc,
                                0,
                                0,
                                width,
                                height,
                                screenDc,
                                x,
                                y,
                                ScreenNativeMethods.Srccopy | ScreenNativeMethods.CaptureBlt))
                        {
                            throw new IOException("BitBlt 屏幕拷贝失败，请重试。");
                        }

                        using var image = Image.FromHbitmap(bitmap);
                        using var stream = new MemoryStream();
                        image.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                    finally
                    {
                        ScreenNativeMethods.SelectObject(memoryDc, previous);
                    }
                }
                finally
                {
                    ScreenNativeMethods.DeleteObject(bitmap);
                }
            }
            finally
            {
                ScreenNativeMethods.DeleteDC(memoryDc);
            }
        }
        finally
        {
            ScreenNativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// GDI 窗口捕获回落链：PrintWindow（含现代合成内容）→ 窗口 DC BitBlt。
    /// 可捕获被遮挡窗口；按 GetWindowRect 尺寸渲染后再裁掉 Win10 隐形调整边框。
    /// 失败返回 null。
    /// </summary>
    public static (byte[] Png, int Width, int Height)? TryCaptureWindow(nint windowHandle)
    {
        if (windowHandle == 0 || !ScreenNativeMethods.IsWindowVisible(windowHandle))
        {
            return null;
        }

        if (!ScreenNativeMethods.GetWindowRect(windowHandle, out var windowRect) || windowRect.IsEmpty)
        {
            return null;
        }

        var windowDc = ScreenNativeMethods.GetDC(windowHandle);
        if (windowDc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var width = windowRect.Width;
            var height = windowRect.Height;

            var memoryDc = ScreenNativeMethods.CreateCompatibleDC(windowDc);
            if (memoryDc == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bitmap = ScreenNativeMethods.CreateCompatibleBitmap(windowDc, width, height);
                if (bitmap == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    var previous = ScreenNativeMethods.SelectObject(memoryDc, bitmap);
                    try
                    {
                        var printed = ScreenNativeMethods.PrintWindow(
                            windowHandle,
                            memoryDc,
                            (uint)ScreenNativeMethods.PrintWindowRenderFullContent);
                        if (!printed)
                        {
                            printed = ScreenNativeMethods.BitBlt(
                                memoryDc,
                                0,
                                0,
                                width,
                                height,
                                windowDc,
                                0,
                                0,
                                ScreenNativeMethods.Srccopy);
                        }

                        if (!printed)
                        {
                            return null;
                        }

                        using var full = Image.FromHbitmap(bitmap);

                        // Win10 的 GetWindowRect 含隐形调整边框；按 DWM 可见边界裁剪。
                        var offsetX = 0;
                        var offsetY = 0;
                        var cropWidth = width;
                        var cropHeight = height;
                        if (ScreenNativeMethods.DwmGetWindowAttribute(
                                windowHandle,
                                ScreenNativeMethods.DwmwaExtendedFrameBounds,
                                out var frame,
                                Marshal.SizeOf<ScreenNativeMethods.NativeRect>()) == 0
                            && !frame.IsEmpty)
                        {
                            offsetX = Math.Max(0, frame.Left - windowRect.Left);
                            offsetY = Math.Max(0, frame.Top - windowRect.Top);
                            cropWidth = Math.Min(width - offsetX, frame.Width);
                            cropHeight = Math.Min(height - offsetY, frame.Height);
                        }

                        if (cropWidth <= 0 || cropHeight <= 0)
                        {
                            return null;
                        }

                        using var cropped = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(cropped))
                        {
                            graphics.DrawImage(
                                full,
                                new Rectangle(0, 0, cropWidth, cropHeight),
                                new Rectangle(offsetX, offsetY, cropWidth, cropHeight),
                                GraphicsUnit.Pixel);
                        }

                        using var stream = new MemoryStream();
                        cropped.Save(stream, ImageFormat.Png);
                        return (stream.ToArray(), cropWidth, cropHeight);
                    }
                    finally
                    {
                        ScreenNativeMethods.SelectObject(memoryDc, previous);
                    }
                }
                finally
                {
                    ScreenNativeMethods.DeleteObject(bitmap);
                }
            }
            finally
            {
                ScreenNativeMethods.DeleteDC(memoryDc);
            }
        }
        catch (Exception ex) when (ex is OutOfMemoryException or InvalidOperationException or ExternalException)
        {
            return null;
        }
        finally
        {
            ScreenNativeMethods.ReleaseDC(windowHandle, windowDc);
        }
    }

    /// <summary>
    /// 最终兜底：从整屏 BitBlt 结果中按窗口可见边界裁剪。
    /// 屏幕 DC 永远有内容，可保证窗口截图至少出图；代价是被遮挡时抓到遮挡物，
    /// 且不能捕获完全位于屏幕外的窗口（枚举阶段已排除最小化窗口）。
    /// 坐标为物理像素，与虚拟屏求交后支持多显示器与负坐标。
    /// </summary>
    public static (byte[] Png, int Width, int Height)? TryCaptureWindowFromScreen(nint windowHandle)
    {
        if (windowHandle == 0 || !ScreenNativeMethods.IsWindowVisible(windowHandle))
        {
            return null;
        }

        var bounds = ScreenNativeMethods.DwmGetWindowAttribute(
            windowHandle,
            ScreenNativeMethods.DwmwaExtendedFrameBounds,
            out var frame,
            Marshal.SizeOf<ScreenNativeMethods.NativeRect>()) == 0
            && !frame.IsEmpty
                ? frame
                : ScreenNativeMethods.GetWindowRect(windowHandle, out var fallbackRect) && !fallbackRect.IsEmpty
                    ? fallbackRect
                    : default;

        if (bounds.IsEmpty)
        {
            return null;
        }

        var virtualScreen = ScreenNativeMethods.VirtualScreen();
        var left = Math.Max(bounds.Left, virtualScreen.Left);
        var top = Math.Max(bounds.Top, virtualScreen.Top);
        var right = Math.Min(bounds.Right, virtualScreen.Right);
        var bottom = Math.Min(bounds.Bottom, virtualScreen.Bottom);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        try
        {
            var png = CaptureScreenRegionToPng(left, top, width, height);
            return (png, width, height);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
