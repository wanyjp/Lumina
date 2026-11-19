using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Domain.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// Windows.Graphics.Capture 窗口捕获：优先路径，可捕获被遮挡与
/// 硬件加速合成内容。D3D11 设备经 DXGI 包装为 WinRT 设备，
/// 帧经 SoftwareBitmap 拷贝到 CPU 后编码 PNG。
/// 任何一步失败返回 null，由调用方回落 GDI。
/// </summary>
internal static class WgcCapture
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = true)]
    private static extern int CreateD3D11Device(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        int featureLevelCount,
        int sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    private const int DriverTypeHardware = 1;
    private const int DriverTypeSoftware = 5;
    private const uint DeviceFlagBgraSupport = 0x20;
    private const int SdkVersion = 7;

    /// <summary>捕获指定窗口；超时 4 秒。失败返回 null。</summary>
    public static (byte[] Png, int Width, int Height)? TryCaptureWindow(nint windowHandle, int timeoutMs = 4000)
    {
        if (windowHandle == 0)
        {
            return null;
        }

        // 能力检测先行：不支持时快速回落到 GDI/屏幕裁剪，避免白等超时。
        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            var item = CreateItemForWindow(windowHandle);
            if (item is null)
            {
                return null;
            }

            var device = CreateWinRtDevice();
            if (device is null)
            {
                DisposeIfPossible(item);
                return null;
            }

            try
            {
                return CaptureItemToPng(item, device, windowHandle, timeoutMs);
            }
            finally
            {
                // 19041 投影下 GraphicsCaptureItem/IDirect3DDevice 未必实现 IDisposable；
                // 按运行时能力释放，未实现时交给 RCW 终结器兜底。
                DisposeIfPossible(device);
                DisposeIfPossible(item);
            }
        }
        catch (Exception)
        {
            // WGC 路径的任何异常都不上抛：回落 GDI 由调用方兜底。
            return null;
        }
    }

    private static void DisposeIfPossible(object value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static (byte[] Png, int Width, int Height)? CaptureItemToPng(
        GraphicsCaptureItem item,
        IDirect3DDevice device,
        nint windowHandle,
        int timeoutMs)
    {
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using (framePool)
        {
            using var session = framePool.CreateCaptureSession(item);
            TryConfigureSession(session);

            var completion = new TaskCompletionSource<(byte[] Bgra, int Width, int Height)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            framePool.FrameArrived += (pool, _) =>
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    using var frame = pool.TryGetNextFrame();
                    if (frame is null)
                    {
                        return;
                    }

                    using var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                    if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                    {
                        return;
                    }

                    var (bgra, frameWidth, frameHeight) = CopyBitmapBytes(bitmap);

                    // WGC 帧可能整体透明（alpha=0，premultiplied 后 RGB 也为 0），
                    // 先强制不透明，再剔除首帧空帧/黑帧，等下一帧有内容再提交。
                    CaptureFrameValidator.ForceOpaque(bgra);
                    if (CaptureFrameValidator.IsBlankBgra(bgra, frameWidth, frameHeight))
                    {
                        return;
                    }

                    completion.TrySetResult((bgra, frameWidth, frameHeight));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            };

            session.StartCapture();

            // 静止的 GPU 窗口可能长时间不产生新帧；主动请求一次重绘再等待首帧。
            ScreenNativeMethods.ForceWindowRedraw(windowHandle);

            if (!completion.Task.Wait(timeoutMs))
            {
                return null;
            }

            var (bgra, width, height) = completion.Task.Result;
            return (GdiCapture.EncodeBgraToPng(bgra, width, height), width, height);
        }
    }

    /// <summary>关闭光标采集（尽力而为，失败忽略）。</summary>
    private static void TryConfigureSession(GraphicsCaptureSession session)
    {
        try
        {
            session.IsCursorCaptureEnabled = false;
        }
        catch (Exception)
        {
            // 旧系统不支持该属性：保持默认。
        }

        // 注：IsBorderRequired（去系统黄框）在 19041 投影中不存在，需 20348+ 契约；
        // 截图只抓一帧，黄框一闪而过，这里保留默认值。持续捕获的小组件另有处理，
        // 见 Lumina.Widgets/Glass/WgcGlassFrameSource.TrySuppressCaptureBorder。
    }

    /// <summary>一次帧拷贝允许的最大字节数；超出视为异常尺寸，放弃 WGC 回落 GDI。</summary>
    private const long MaxFrameBytes = 512L * 1024 * 1024;

    private static (byte[] Bgra, int Width, int Height) CopyBitmapBytes(SoftwareBitmap bitmap)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;

        // 用 long 计算并按上限校验：窗口尺寸极大时 int 会溢出成错误的缓冲长度。
        var capacityLong = (long)width * height * 4;
        if (capacityLong <= 0 || capacityLong > MaxFrameBytes)
        {
            throw new InvalidOperationException($"WGC 帧尺寸异常：{width}×{height}");
        }

        var capacity = (uint)capacityLong;
        var buffer = new Windows.Storage.Streams.Buffer(capacity);
        bitmap.CopyToBuffer(buffer);

        var bytes = new byte[capacity];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return (bytes, width, height);
    }

    private static GraphicsCaptureItem? CreateItemForWindow(nint windowHandle)
    {
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = GraphicsCaptureItemIid;
            var abi = interop.CreateForWindow(windowHandle, ref iid);
            if (abi == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return GraphicsCaptureItem.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IDirect3DDevice? CreateWinRtDevice()
    {
        var hr = CreateD3D11Device(
            IntPtr.Zero,
            DriverTypeHardware,
            IntPtr.Zero,
            DeviceFlagBgraSupport,
            IntPtr.Zero,
            0,
            SdkVersion,
            out var device,
            out _,
            out var context);
        if (hr < 0)
        {
            hr = CreateD3D11Device(
                IntPtr.Zero,
                DriverTypeSoftware,
                IntPtr.Zero,
                DeviceFlagBgraSupport,
                IntPtr.Zero,
                0,
                SdkVersion,
                out device,
                out _,
                out context);
        }

        if (hr < 0 || device == IntPtr.Zero)
        {
            return null;
        }

        Marshal.Release(context);
        try
        {
            var iid = DxgiDeviceIid;
            if (Marshal.QueryInterface(device, in iid, out var dxgiDevice) != 0 || dxgiDevice == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                if (CreateDirect3D11DeviceFromDxgiDevice(dxgiDevice, out var inspectable) < 0
                    || inspectable == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(device);
        }
    }
}
