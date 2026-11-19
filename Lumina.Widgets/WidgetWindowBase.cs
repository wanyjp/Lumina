using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Widgets;
using Lumina.Widgets.Glass;
using Microsoft.Win32;

namespace Lumina.Widgets;

/// <summary>
/// 小组件窗口基类：透明无边框、置顶、整窗/空白区点击穿透、拖动与边缘吸附、
/// 显示器拓扑变化后的位置校正。具体卡片内容由派生窗口实现。
/// </summary>
public abstract class WidgetWindowBase : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const double SnapThreshold = 18;

    /// <summary>逐帧捕获时的小图宽度，与 WGC 帧源保持一致，避免两条路径交替写不同尺寸的纹理。</summary>
    private const int FrameBackdropWidth = 140;

    /// <summary>采样模式的小图宽度。</summary>
    private const int SampledBackdropWidth = 240;

    private readonly Action<WidgetState> _stateChanged;
    private readonly Action<WidgetKind> _closed;

    private HwndSource? _source;
    private GlassRefreshScheduler? _glassScheduler;
    private DispatcherTimer? _dragRefreshTimer;
    private readonly WidgetBackdropSampler _glassSampler = new();
    private WgcGlassFrameSource? _frameSource;
    private bool _frameCaptureEnabled;
    private bool _captureBusy;
    private int _changeStreak;
    private bool _glassFallback;
    private bool _suppressState;
    private bool _disposed;

    protected WidgetWindowBase(
        WidgetState state,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stateChanged);
        ArgumentNullException.ThrowIfNull(closed);

        State = state;
        _stateChanged = stateChanged;
        _closed = closed;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;

        Left = state.X;
        Top = state.Y;
        Width = state.Width;
        Height = state.Height;
        Opacity = state.Opacity;
        Topmost = state.Topmost;

        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        DpiChanged += OnDpiChanged;
        SizeChanged += OnWindowSizeChanged;
        Loaded += (_, _) => ApplySkin();
    }

    public WidgetState State { get; private set; }

    protected abstract FrameworkElement InteractiveElement { get; }

    public void ApplyState(WidgetState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _suppressState = true;
        try
        {
            State = state;
            Left = state.X;
            Top = state.Y;
            Width = state.Width;
            Height = state.Height;
            Opacity = state.Opacity;
            Topmost = state.Topmost;
        }
        finally
        {
            _suppressState = false;
        }

        ApplySkin();
    }

    /// <summary>内容装载后立即套用当前皮肤，避免首帧出现默认卡片底色。</summary>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        ApplySkin();
    }

    private void ApplySkin()
    {
        if (InteractiveElement is not Border card)
        {
            return;
        }

        WidgetSkinVisuals.Apply(card, State.Skin);

        var glass = (Content as IWidgetGlassView)?.GlassSurface;
        if (glass is null)
        {
            return;
        }

        if (SystemParameters.HighContrast
            || State.Skin is not (WidgetSkin.Acrylic or WidgetSkin.LiquidGlass))
        {
            glass.Clear();
            StopGlassSampling();
            StopFrameCapture();
            WidgetGlassCaptureStatus.ReportInactive(State.Kind);
            return;
        }

        glass.ApplySkin(State.Skin);

        // 逐帧捕获（实验）优先；起不来就回到 GDI 采样，功能不降级。
        if (FrameCaptureEnabled)
        {
            if (TryStartFrameCapture())
            {
                // 帧本身已经过像素级降采样，交给双线性放大即可，省掉高斯模糊。
                glass.BlurEnabled = false;
                StopGlassSampling();

                // 位置可能刚被 ApplyState/DPI 校正改过，同步裁剪区域（跨屏会重建会话），
                // 内部会顺带补一帧当前位置的背景。
                UpdateFrameRegion();
                return;
            }

            // 启动失败时 TryStartFrameCapture 已记下回退原因，这里不要清掉它。
            glass.BlurEnabled = true;
            _frameSource?.Stop();
            RefreshGlassBackdrop();
            StartGlassSampling();
            return;
        }

        glass.BlurEnabled = true;
        StopFrameCapture();
        WidgetGlassCaptureStatus.ReportInactive(State.Kind);
        RefreshGlassBackdrop();
        StartGlassSampling();
    }

    /// <summary>
    /// 是否用 WGC 逐帧捕获替代 GDI 采样；由总设置的全局开关驱动。
    /// 只有亚克力/液态玻璃会真正用到它。
    /// </summary>
    public bool FrameCaptureEnabled
    {
        get => _frameCaptureEnabled;
        set
        {
            if (_frameCaptureEnabled == value)
            {
                return;
            }

            _frameCaptureEnabled = value;
            if (!_disposed)
            {
                ApplySkin();
            }
        }
    }

    private bool TryStartFrameCapture()
    {
        if (_disposed || InteractiveElement is not Border card)
        {
            return false;
        }

        if (_frameSource is { IsRunning: true })
        {
            return true;
        }

        var bounds = WidgetBackdropSampler.Measure(this, card);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (bounds is null || hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (_frameSource is null)
        {
            var source = new WgcGlassFrameSource();
            source.FrameArrived += OnGlassFrame;
            source.Failed += OnFrameSourceFailed;
            _frameSource = source;
        }

        var started = _frameSource.Start(hwnd, bounds.Value, 140);
        if (started)
        {
            WidgetGlassCaptureStatus.ReportActive(State.Kind);
        }
        else
        {
            WidgetGlassCaptureStatus.ReportFallback(
                State.Kind,
                _frameSource.FailureReason ?? "捕获启动失败");
        }

        return started;
    }

    private void StopFrameCapture() => _frameSource?.Stop();

    private void OnFrameSourceFailed()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed)
            {
                return;
            }

            var reason = _frameSource?.FailureReason ?? "捕获会话已中断";
            StopFrameCapture();
            WidgetGlassCaptureStatus.ReportFallback(State.Kind, reason);
            if (!SystemParameters.HighContrast
                && State.Skin is WidgetSkin.Acrylic or WidgetSkin.LiquidGlass)
            {
                if (Content is IWidgetGlassView view)
                {
                    view.GlassSurface.BlurEnabled = true;
                }

                StartGlassSampling();
                RefreshGlassBackdrop();
            }
        }));
    }

    /// <summary>逐帧帧到达：拷贝一份再交给界面线程，避免与下一帧写入竞争。</summary>
    private void OnGlassFrame(byte[] pixels, int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return;
        }

        var length = width * height * 4;
        if (pixels.Length < length)
        {
            return;
        }

        var copy = ArrayPool<byte>.Shared.Rent(length);
        Buffer.BlockCopy(pixels, 0, copy, 0, length);
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_disposed)
                    {
                        return;
                    }

                    (Content as IWidgetGlassView)?.GlassSurface
                        .SetBackdrop(copy, width, height, State.Skin);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(copy);
                }
            }));
        }
        catch (TaskCanceledException)
        {
            ArrayPool<byte>.Shared.Return(copy);
        }
    }

    /// <summary>组件移动/换屏后同步裁剪区域；跨显示器时重建会话。</summary>
    private void UpdateFrameRegion(bool refreshBackdrop = true)
    {
        if (_frameSource is not { IsRunning: true } || InteractiveElement is not Border card)
        {
            return;
        }

        var bounds = WidgetBackdropSampler.Measure(this, card);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (bounds is null || hwnd == IntPtr.Zero || !_frameSource.TryUpdateRegion(hwnd, bounds.Value))
        {
            StopFrameCapture();
            if (!TryStartFrameCapture())
            {
                if (Content is IWidgetGlassView view)
                {
                    view.GlassSurface.BlurEnabled = true;
                }

                StartGlassSampling();
                RefreshGlassBackdrop();
            }

            return;
        }

        // 逐帧捕获只在画面内容变化时推帧，位置刚变时必须补一帧，
        // 否则玻璃会停在上一个位置的画面上等下一次内容变化。
        if (refreshBackdrop)
        {
            RefreshGlassBackdrop();
        }
    }

    /// <summary>
    /// 抓取卡片背后的桌面画面并交给模糊层；失败时回退为不透明卡面。
    /// 抓屏与降采样在后台线程完成，界面线程只做一次像素上传，
    /// 因此 30fps 的高频采样也不会卡住拖动。背景帧比对发现画面在动时，
    /// 会通知调度器把节拍抬到高频。
    /// </summary>
    private void RefreshGlassBackdrop(bool force = false)
    {
        if (_disposed || !IsVisible)
        {
            return;
        }

        if (InteractiveElement is not Border card)
        {
            return;
        }

        var glass = (Content as IWidgetGlassView)?.GlassSurface;
        if (glass is null)
        {
            return;
        }

        if (SystemParameters.HighContrast
            || State.Skin is not (WidgetSkin.Acrylic or WidgetSkin.LiquidGlass))
        {
            glass.Clear();
            return;
        }

        var bounds = WidgetBackdropSampler.Measure(this, card);
        if (bounds is null || _captureBusy)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 只在上一帧抓屏失败后恢复皮肤底色，避免每帧重复设置资源。
        if (_glassFallback)
        {
            WidgetSkinVisuals.Apply(card, State.Skin);
            _glassFallback = false;
        }

        _captureBusy = true;
        var sampler = _glassSampler;
        var area = bounds.Value;
        var skin = State.Skin;

        // 逐帧会话运行期间：GDI 只作为拖动时的补充，输出尺寸与 WGC 对齐，
        // 并且抓屏后仍保持“不参与捕获”，避免下一帧把组件自己拍进去。
        var frameCapture = _frameSource is { IsRunning: true };
        var widthLimit = frameCapture ? FrameBackdropWidth : SampledBackdropWidth;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var captured = false;
            var width = 0;
            var height = 0;
            var changed = false;
            try
            {
                captured = sampler.TryCapture(
                    hwnd,
                    area,
                    out width,
                    out height,
                    out changed,
                    widthLimit,
                    frameCapture);
            }
            catch (ExternalException)
            {
                captured = false;
            }

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _captureBusy = false;
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        ApplyCapturedBackdrop(captured, width, height, changed, force, glass, sampler, skin);
                    }
                    catch (Exception)
                    {
                        // 退出期界面资源已释放：不得让异常从调度回调逃出。
                    }
                }));
            }
            catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
            {
                // 调度器已关闭：复位忙标志，避免玻璃采样永久停摆。
                _captureBusy = false;
            }
        });
    }

    private void ApplyCapturedBackdrop(
        bool captured,
        int width,
        int height,
        bool changed,
        bool force,
        WidgetGlassSurface glass,
        WidgetBackdropSampler sampler,
        WidgetSkin skin)
    {
        if (captured)
        {
            // 画面没变就不重复上传纹理，静止时只花一次缩小与比对的成本；
            // 拖动时位置在变，即使像素差异低于阈值也必须刷新。
            if (changed || force || !glass.HasFrame)
            {
                glass.SetBackdrop(sampler.Pixels, width, height, skin);
            }

            // 只有连续两帧都在变才认为“背景在动”（视频/滚动）；
            // 每秒跳一次的时钟、闪烁的光标只会命中一帧，不把节拍顶到 30fps。
            _changeStreak = changed ? _changeStreak + 1 : 0;
            if (_changeStreak >= 2)
            {
                _glassScheduler?.NotifyActivity();
            }

            return;
        }

        glass.Clear();
        _glassFallback = true;
        if (InteractiveElement is Border current)
        {
            WidgetSkinVisuals.ApplyFallback(current);
        }
    }

    private void StartDragRefresh()
    {
        if (_disposed)
        {
            return;
        }

        if (_dragRefreshTimer is null)
        {
            _dragRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33),
            };
            _dragRefreshTimer.Tick += (_, _) => RefreshWhileDragging();
        }

        if (!_dragRefreshTimer.IsEnabled)
        {
            _dragRefreshTimer.Start();
        }
    }

    private void StopDragRefresh() => _dragRefreshTimer?.Stop();

    /// <summary>拖动中的一次刷新：先同步裁剪区域，再抓一帧当前位置的背景。</summary>
    private void RefreshWhileDragging()
    {
        if (_disposed)
        {
            return;
        }

        if (_frameSource is { IsRunning: true })
        {
            UpdateFrameRegion(refreshBackdrop: false);
        }

        RefreshGlassBackdrop(force: true);
    }

    private void StartGlassSampling()
    {
        if (_disposed)
        {
            return;
        }

        _glassScheduler ??= new GlassRefreshScheduler(() => RefreshGlassBackdrop());
        if (!_glassScheduler.IsRunning)
        {
            _glassScheduler.Start();
        }
    }

    private void StopGlassSampling()
    {
        _glassScheduler?.Stop();
        _changeStreak = 0;
    }

    protected void DragByCard()
    {
        StopGlassSampling();

        // 拖动过程中按 33ms 持续采样：逐帧捕获只在画面内容变化时推帧，
        // 组件自己在移动时系统并不会产帧，必须用 GDI 采样补上这一段。
        StartDragRefresh();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标未按下或窗口状态变化时忽略。
        }
        finally
        {
            StopDragRefresh();
        }

        SnapToEdges();
        _glassScheduler?.NotifyActivity();
        if (_frameSource is { IsRunning: true })
        {
            UpdateFrameRegion();
            RefreshGlassBackdrop();
            return;
        }

        RefreshGlassBackdrop();
        if (!SystemParameters.HighContrast
            && State.Skin is WidgetSkin.Acrylic or WidgetSkin.LiquidGlass)
        {
            StartGlassSampling();
        }
    }

    protected void SnapToEdges()
    {
        var workArea = WidgetScreen.GetWorkArea(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var left = Left;
        var top = Top;

        if (Math.Abs(left - workArea.Left) <= SnapThreshold)
        {
            left = workArea.Left;
        }
        else if (Math.Abs(workArea.Right - (left + width)) <= SnapThreshold)
        {
            left = workArea.Right - width;
        }

        if (Math.Abs(top - workArea.Top) <= SnapThreshold)
        {
            top = workArea.Top;
        }
        else if (Math.Abs(workArea.Bottom - (top + height)) <= SnapThreshold)
        {
            top = workArea.Bottom - height;
        }

        if (left != Left || top != Top)
        {
            Left = left;
            Top = top;
        }

        SaveStateFromWindow();
    }

    protected void SaveStateFromWindow()
    {
        if (_suppressState)
        {
            return;
        }

        State = State with
        {
            X = Left,
            Y = Top,
            Width = ActualWidth > 0 ? ActualWidth : Width,
            Height = ActualHeight > 0 ? ActualHeight : Height,
            Opacity = Opacity,
            Topmost = Topmost,
        };
        _stateChanged(State);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        _source.AddHook(WndProc);
        Dispatcher.BeginInvoke(new Action(ClampToVisibleArea));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(ClampToVisibleArea));
    }

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(ClampToVisibleArea));
    }

    /// <summary>唤醒或显示器配置变化后，由宿主主动把窗口收敛到当前屏幕可见区域。</summary>
    public void ConvergeToVisibleArea() => ClampToVisibleArea();

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed || _suppressState)
        {
            return;
        }

        // 内容驱动的尺寸变化：重新校正位置、抓取新区域的玻璃背景并持久化实际尺寸。
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ClampToVisibleArea();
                SaveStateFromWindow();
            }),
            DispatcherPriority.Background);
    }

    private void ClampToVisibleArea()
    {
        if (_disposed)
        {
            return;
        }

        var workArea = WidgetScreen.GetWorkArea(this);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        // 自适应高度的窗口在首帧前可能还没有尺寸，等布局完成后的 SizeChanged 再校正。
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }

        var (left, top) = WidgetPlacement.Clamp(
            Left,
            Top,
            width,
            height,
            new PlacementArea(workArea.Left, workArea.Top, workArea.Width, workArea.Height));

        if (Math.Abs(left - Left) > 0.5 || Math.Abs(top - Top) > 0.5)
        {
            Left = left;
            Top = top;
            SaveStateFromWindow();
        }

        if (_frameSource is { IsRunning: true })
        {
            UpdateFrameRegion();
            RefreshGlassBackdrop();
            return;
        }

        RefreshGlassBackdrop();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        if (State.ClickThrough)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        try
        {
            var screenX = unchecked((short)(long)lParam);
            var screenY = unchecked((short)(((long)lParam) >> 16));
            var point = PointFromScreen(new Point(screenX, screenY));
            var bounds = InteractiveElement
                .TransformToAncestor(this)
                .TransformBounds(new Rect(InteractiveElement.RenderSize));

            if (!bounds.Contains(point))
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
        }
        catch (InvalidOperationException)
        {
            // 窗口尚未完成布局时按默认可命中处理。
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _disposed = true;
        _glassScheduler?.Dispose();
        _glassScheduler = null;
        _frameSource?.Dispose();
        _frameSource = null;
        _dragRefreshTimer?.Stop();
        _dragRefreshTimer = null;
        WidgetGlassCaptureStatus.ReportInactive(State.Kind);
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        DpiChanged -= OnDpiChanged;

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _closed(State.Kind);
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
        }
    }
}
