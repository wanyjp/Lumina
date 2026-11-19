using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Lumina.Widgets.Glass;

/// <summary>
/// Windows.Graphics.Capture 逐帧帧源（实验）：
/// 按显示器建立捕获会话，用 D3D11 的 CopySubresourceRegion 在 GPU 上裁到组件区域，
/// 再把小图回读为 BGRA 像素交给玻璃层。帧由系统在内容变化时推送，静止桌面不产帧。
///
/// 捕获期间窗口被标记为“不参与捕获”，因此组件不会拍到自己，代价是屏幕录制里看不到组件。
/// 任一步失败都会抛 <see cref="Failed"/>，由调用方回退到 GDI 采样。
/// </summary>
internal sealed class WgcGlassFrameSource : IDisposable
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private const uint WdaNone = 0x0;
    private const uint WdaExcludeFromCapture = 0x11;
    private const int MonitorDefaultToNearest = 0x00000002;
    private const double Saturation = 1.18;
    private const int BrightnessLift = 4;

    /// <summary>玻璃层只按 30fps 消费帧；更高刷新率的显示器直接丢帧。</summary>
    private const int FrameIntervalMs = 33;

    private readonly object _sync = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;

    private IntPtr _excludeWindow;
    private CaptureBounds _region;
    private int _targetWidth = 240;
    private int _stagingWidth;
    private int _stagingHeight;
    private int _frameWidth;
    private int _frameHeight;
    private MONITORINFO _monitor;
    private byte[] _stage = Array.Empty<byte>();
    private byte[] _pixels = Array.Empty<byte>();
    private byte[] _smoothed = Array.Empty<byte>();
    private long _lastFrameTimestamp;
    private int _busy;
    private bool _running;
    private bool _disposed;

    /// <summary>后台线程回调：BGRA 小图与尺寸。</summary>
    public event Action<byte[], int, int>? FrameArrived;

    /// <summary>捕获不可用或设备丢失；调用方应停止并回退。</summary>
    public event Action? Failed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _running;
            }
        }
    }

    /// <summary>最近一次启动失败的原因（用于在设置里说明回退原因）；成功时为 null。</summary>
    public string? FailureReason { get; private set; }

    /// <summary>启动或重启会话：<paramref name="region"/> 为屏幕像素坐标。</summary>
    public bool Start(IntPtr excludeWindow, CaptureBounds region, int targetWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();
        FailureReason = null;
        if (excludeWindow == IntPtr.Zero || region.Width < 8 || region.Height < 8)
        {
            FailureReason = "组件区域无效";
            return false;
        }

        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                FailureReason = "系统不支持 Windows 图形捕获";
                return false;
            }

            var monitorHandle = MonitorFromPoint(
                new NativePoint
                {
                    X = region.Left + (region.Width / 2),
                    Y = region.Top + (region.Height / 2),
                },
                MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
            {
                FailureReason = "找不到组件所在显示器";
                return false;
            }

            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitorHandle, ref info))
            {
                FailureReason = "读取显示器信息失败";
                return false;
            }

            var item = CreateItemForMonitor(monitorHandle);
            if (item is null)
            {
                FailureReason = "无法创建显示器捕获项";
                return false;
            }

            if (!EnsureDevice())
            {
                FailureReason = "创建 D3D11 捕获设备失败";
                return false;
            }

            var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            var session = pool.CreateCaptureSession(item);
            TryConfigureSession(session);

            lock (_sync)
            {
                _excludeWindow = excludeWindow;
                _region = region;
                _targetWidth = Math.Max(48, targetWidth);
                _monitor = info;
                _item = item;
                _framePool = pool;
                _session = session;
                _frameWidth = item.Size.Width;
                _frameHeight = item.Size.Height;
                _running = true;
            }

            pool.FrameArrived += OnFrameArrived;
            session.StartCapture();

            // 会话运行期间保持排除自身，避免捕获到组件自己。
            SetWindowDisplayAffinity(excludeWindow, WdaExcludeFromCapture);
            return true;
        }
        catch (Exception)
        {
            FailureReason = "创建捕获会话失败（显卡驱动或系统策略限制）";
            Stop();
            return false;
        }
    }

    /// <summary>
    /// 更新裁剪区域（组件移动后调用）。跨显示器或会话已失效时返回 false，由调用方重建。
    /// </summary>
    public bool TryUpdateRegion(IntPtr hwnd, CaptureBounds region)
    {
        lock (_sync)
        {
            if (!_running || hwnd == IntPtr.Zero || hwnd != _excludeWindow)
            {
                return false;
            }

            var monitorHandle = MonitorFromPoint(
                new NativePoint
                {
                    X = region.Left + (region.Width / 2),
                    Y = region.Top + (region.Height / 2),
                },
                MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
            {
                return false;
            }

            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitorHandle, ref info))
            {
                return false;
            }

            if (info.Monitor.Left != _monitor.Monitor.Left
                || info.Monitor.Top != _monitor.Monitor.Top
                || info.Monitor.Right != _monitor.Monitor.Right
                || info.Monitor.Bottom != _monitor.Monitor.Bottom)
            {
                return false;
            }

            _region = region;
            return true;
        }
    }

    public void Stop()
    {
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        GraphicsCaptureItem? item;
        IntPtr excludeWindow;

        lock (_sync)
        {
            _running = false;
            pool = _framePool;
            session = _session;
            item = _item;
            excludeWindow = _excludeWindow;
            _framePool = null;
            _session = null;
            _item = null;
            _excludeWindow = IntPtr.Zero;
        }

        if (pool is not null)
        {
            pool.FrameArrived -= OnFrameArrived;
        }

        try
        {
            session?.Dispose();
        }
        catch (Exception)
        {
            // 关闭会话的失败不影响后续回退。
        }

        try
        {
            pool?.Dispose();
        }
        catch (Exception)
        {
            // 同上。
        }

        DisposeIfPossible(item);

        if (excludeWindow != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(excludeWindow, WdaNone);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        DisposeIfPossible(_winrtDevice);
        _winrtDevice = null;
        _staging?.Dispose();
        _staging = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        // 帧池只有 2 个缓冲：不把帧取走，缓冲占满后系统就不再触发 FrameArrived，
        // 表现为组件背景定格在第一帧。所以每次回调都先把池取空，只保留最新一帧。
        Direct3D11CaptureFrame? latest = null;
        try
        {
            while (true)
            {
                var frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    break;
                }

                latest?.Dispose();
                latest = frame;
            }
        }
        catch (Exception)
        {
            latest?.Dispose();
            FailureReason = "读取捕获帧失败（设备丢失或被系统拒绝）";
            Failed?.Invoke();
            return;
        }

        if (latest is null)
        {
            return;
        }

        try
        {
            // 系统按显示器刷新率推帧（120Hz 屏会到 120fps），玻璃层只需要 30fps，
            // 超出的帧取到就丢，避免软件合成路径上的上传与平滑成倍开销。
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastFrameTimestamp != 0
                && (now - _lastFrameTimestamp) * 1000 / System.Diagnostics.Stopwatch.Frequency < FrameIntervalMs)
            {
                return;
            }

            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                return;
            }

            _lastFrameTimestamp = now;
            try
            {
                if (!TryReadFrame(latest, out var pixels, out var width, out var height))
                {
                    return;
                }

                FrameArrived?.Invoke(pixels, width, height);
            }
            catch (Exception)
            {
                FailureReason = "解析捕获帧失败（设备丢失或被系统拒绝）";
                Failed?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
        finally
        {
            latest.Dispose();
        }
    }

    private bool TryReadFrame(
        Direct3D11CaptureFrame frame,
        out byte[] pixels,
        out int width,
        out int height)
    {
        pixels = Array.Empty<byte>();
        width = 0;
        height = 0;

        CaptureBounds region;
        MONITORINFO monitor;
        int targetWidth;
        lock (_sync)
        {
            if (!_running || _device is null || _context is null)
            {
                return false;
            }

            region = _region;
            monitor = _monitor;
            targetWidth = _targetWidth;
        }

        if (frame.Surface is null)
        {
            return false;
        }

        using var texture = GetTexture(frame.Surface);
        if (texture is null)
        {
            return false;
        }

        var description = texture.Description;
        if (description.Width == 0 || description.Height == 0)
        {
            return false;
        }

        // 显示器坐标 → 帧内坐标；系统可能在 DPI 变化时缩放帧，按比例换算。
        var scaleX = description.Width / (double)Math.Max(1, _frameWidth);
        var scaleY = description.Height / (double)Math.Max(1, _frameHeight);
        var left = (int)Math.Round((region.Left - monitor.Monitor.Left) * scaleX);
        var top = (int)Math.Round((region.Top - monitor.Monitor.Top) * scaleY);
        var right = left + (int)Math.Round(region.Width * scaleX);
        var bottom = top + (int)Math.Round(region.Height * scaleY);

        var framePixelWidth = (int)description.Width;
        var framePixelHeight = (int)description.Height;
        left = Math.Clamp(left, 0, framePixelWidth - 1);
        top = Math.Clamp(top, 0, framePixelHeight - 1);
        right = Math.Clamp(right, left + 1, framePixelWidth);
        bottom = Math.Clamp(bottom, top + 1, framePixelHeight);

        var cropWidth = right - left;
        var cropHeight = bottom - top;
        EnsureStaging(cropWidth, cropHeight);
        if (_staging is null || _context is null)
        {
            return false;
        }

        _context.CopySubresourceRegion(
            _staging,
            0,
            0,
            0,
            0,
            texture,
            0,
            new Box(left, top, 0, right, bottom, 1));

        var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var size = cropWidth * cropHeight * 4;
            if (_stage.Length < size)
            {
                _stage = new byte[size];
            }

            if (mapped.RowPitch == cropWidth * 4)
            {
                Marshal.Copy(mapped.DataPointer, _stage, 0, size);
            }
            else
            {
                for (var row = 0; row < cropHeight; row++)
                {
                    var source = IntPtr.Add(mapped.DataPointer, row * (int)mapped.RowPitch);
                    Marshal.Copy(source, _stage, row * cropWidth * 4, cropWidth * 4);
                }
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        Diffuse(cropWidth, cropHeight, targetWidth, out width, out height);
        Smooth(width, height);
        pixels = _pixels;
        return width > 0 && height > 0;
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingWidth == width && _stagingHeight == height)
        {
            return;
        }

        _staging?.Dispose();
        _staging = null;
        if (_device is null)
        {
            return;
        }

        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
        _stagingWidth = width;
        _stagingHeight = height;
    }

    /// <summary>盒式降采样 + 轻微提色，与 GDI 采样保持同一条观感。</summary>
    private void Diffuse(int width, int height, int targetWidth, out int smallWidth, out int smallHeight)
    {
        var factor = Math.Max(1, (int)Math.Round(width / (double)targetWidth));
        var outWidth = Math.Max(4, width / factor);
        var outHeight = Math.Max(4, height / factor);
        var size = outWidth * outHeight * 4;
        if (_pixels.Length != size)
        {
            _pixels = new byte[size];
        }

        var stride = width * 4;
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                int blue = 0, green = 0, red = 0, count = 0;
                for (var offsetY = 0; offsetY < factor; offsetY++)
                {
                    var sourceY = (y * factor) + offsetY;
                    if (sourceY >= height)
                    {
                        break;
                    }

                    var row = sourceY * stride;
                    for (var offsetX = 0; offsetX < factor; offsetX++)
                    {
                        var sourceX = (x * factor) + offsetX;
                        if (sourceX >= width)
                        {
                            break;
                        }

                        var index = row + (sourceX * 4);
                        blue += _stage[index];
                        green += _stage[index + 1];
                        red += _stage[index + 2];
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var averageRed = red / (double)count;
                var averageGreen = green / (double)count;
                var averageBlue = blue / (double)count;
                var gray = (0.299 * averageRed) + (0.587 * averageGreen) + (0.114 * averageBlue);
                var target = ((y * outWidth) + x) * 4;
                _pixels[target] = Clamp(averageBlue, gray);
                _pixels[target + 1] = Clamp(averageGreen, gray);
                _pixels[target + 2] = Clamp(averageRed, gray);
                _pixels[target + 3] = 0xFF;
            }
        }

        smallWidth = outWidth;
        smallHeight = outHeight;
    }

    private static byte Clamp(double value, double gray)
    {
        var boosted = gray + ((value - gray) * Saturation) + BrightnessLift;
        return (byte)Math.Clamp((int)Math.Round(boosted), 0, 255);
    }

    /// <summary>
    /// 小图上的 1-2-1 分离式平滑：抵消 4 倍放大后的块状边缘，
    /// 比 WPF 的 BlurEffect 便宜两个数量级（分层窗口是软件合成）。
    /// </summary>
    private void Smooth(int width, int height)
    {
        if (width < 3 || height < 3)
        {
            return;
        }

        var size = width * height * 4;
        if (_smoothed.Length != size)
        {
            _smoothed = new byte[size];
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var index = row + (x * 4);
                var left = row + (Math.Max(0, x - 1) * 4);
                var right = row + (Math.Min(width - 1, x + 1) * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    _smoothed[index + channel] = (byte)(
                        (_pixels[left + channel] + (2 * _pixels[index + channel]) + _pixels[right + channel]) / 4);
                }

                _smoothed[index + 3] = 0xFF;
            }
        }

        for (var y = 0; y < height; y++)
        {
            var up = Math.Max(0, y - 1) * width * 4;
            var down = Math.Min(height - 1, y + 1) * width * 4;
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var index = row + (x * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    _pixels[index + channel] = (byte)(
                        (_smoothed[up + (x * 4) + channel]
                         + (2 * _smoothed[index + channel])
                         + _smoothed[down + (x * 4) + channel]) / 4);
                }
            }
        }
    }

    private bool EnsureDevice()
    {
        if (_device is not null && _context is not null && _winrtDevice is not null)
        {
            return true;
        }

        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null!,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context);
        if (result.Failure || device is null || context is null)
        {
            device?.Dispose();
            context?.Dispose();
            return false;
        }

        try
        {
            using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
            if (CreateDirect3D11DeviceFromDxgiDevice(dxgiDevice.NativePointer, out var inspectable) < 0
                || inspectable == IntPtr.Zero)
            {
                device.Dispose();
                context.Dispose();
                return false;
            }

            try
            {
                _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }

            _device = device;
            _context = context!;
            return true;
        }
        catch (Exception)
        {
            device.Dispose();
            context.Dispose();
            return false;
        }
    }

    private static ID3D11Texture2D? GetTexture(IDirect3DSurface surface)
    {
        try
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var iid = typeof(ID3D11Texture2D).GUID;
            var pointer = access.GetInterface(ref iid);
            return pointer == IntPtr.Zero ? null : new ID3D11Texture2D(pointer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static GraphicsCaptureItem? CreateItemForMonitor(IntPtr monitor)
    {
        try
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var iid = GraphicsCaptureItemIid;
            var abi = interop.CreateForMonitor(monitor, ref iid);
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

        TrySuppressCaptureBorder(session);
    }

    /// <summary>
    /// 关掉系统为捕获会话绘制的黄色边框。
    /// 该能力在 <c>IGraphicsCaptureSession3</c>（UniversalApiContract v12 / Win11 22H2 起）上，
    /// 而本项目钉住的 19041 投影里没有 <c>IsBorderRequired</c>，因此手写接口后
    /// QueryInterface 取用；旧系统 QI 失败或系统策略拒绝时静默保留边框，不影响捕获本身。
    /// </summary>
    private static void TrySuppressCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            session.As<IGraphicsCaptureSession3>().IsBorderRequired = 0;
        }
        catch (Exception)
        {
            // Win10 与旧版 Win11 没有该接口：边框是系统绘制，只能保留。
        }
    }

    private static void DisposeIfPossible(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(ref Guid iid);
    }

    /// <summary>
    /// <c>IGraphicsCaptureSession3</c> 的最小定义（UUID 见 UniversalApiContract v12）：
    /// 只用到 <c>IsBorderRequired</c>。该接口派生自 IInspectable，而 .NET 5 起不再支持
    /// <see cref="ComInterfaceType.InterfaceIsIInspectable"/> 的调用，因此按官方建议改用
    /// IUnknown 并补三个占位方法，让属性落在正确的 vtable 槽位上。
    /// </summary>
    [ComImport]
    [Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession3
    {
        // 以下三个方法只用于占位（对应 IInspectable::GetIids / GetRuntimeClassName / GetTrustLevel），不要调用。
        void GetIids(out int iidCount, out IntPtr iids);

        void GetRuntimeClassName(out IntPtr className);

        void GetTrustLevel(out int trustLevel);

        /// <summary>ABI 上的 boolean（4 字节整数）：0 表示不绘制捕获边框。</summary>
        int IsBorderRequired { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
