using System.IO;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// <see cref="IScreenCaptureService"/> 的 Windows 实现。
/// 区域 = 遮罩选区 + GDI BitBlt；全屏 = 虚拟屏 BitBlt（含多显示器与负坐标）；
/// 窗口 = Windows.Graphics.Capture 优先、PrintWindow/BitBlt 回落；
/// 延时 = 倒计时覆盖层 + 全屏捕获。捕获失败抛出可读异常，用户取消抛出
/// <see cref="OperationCanceledException"/>，均不静默返回空图。
/// </summary>
public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    /// <summary>覆盖层退场到 DWM 完成合成所需的等待时间。</summary>
    private static readonly TimeSpan OverlayTeardownDelay = TimeSpan.FromMilliseconds(280);

    public async Task<CaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        var selection = await Task.Run(
            () => RegionSelectionOverlay.Run(cancellationToken),
            cancellationToken).ConfigureAwait(true);
        if (selection is null)
        {
            throw new OperationCanceledException("用户取消了区域截图。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 等分层窗口退场完成合成，避免压暗残影进入截图。
        await Task.Delay(OverlayTeardownDelay, cancellationToken).ConfigureAwait(true);

        return await Task.Run(
            () => CaptureRectangle(selection.Value, CaptureType.Region),
            cancellationToken).ConfigureAwait(true);
    }

    public Task<CaptureResult> CaptureFullScreenAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () => CaptureRectangle(ScreenNativeMethods.VirtualScreen(), CaptureType.FullScreen),
            cancellationToken);

    public Task<CaptureResult> CaptureWindowAsync(nint windowHandle, CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (windowHandle == 0)
                {
                    throw new IOException("窗口句柄无效，无法截图。");
                }

                // WGC 是唯一能捕获被遮挡窗口真实内容的主路径：给足首帧时间，
                // 并在静止窗口上主动请求重绘（见 WgcCapture）。
                var wgc = WgcCapture.TryCaptureWindow(windowHandle, timeoutMs: 3500);
                if (wgc is { } wgcFrame && !IsBlank(wgcFrame.Png))
                {
                    return BuildWindowResult(wgcFrame);
                }

                var gdi = GdiCapture.TryCaptureWindow(windowHandle);
                if (gdi is { } gdiFrame && !IsBlank(gdiFrame.Png))
                {
                    return BuildWindowResult(gdiFrame);
                }

                // 屏幕裁剪是最后兜底，但直接裁当前屏幕会把前景窗口/桌面当成目标窗口。
                // 因此先隐藏本进程置顶窗口、把目标窗口激活到前台，并在激活后重试一次 WGC
                // （重绘后常能拿到真帧），再裁剪屏幕。
                // 记录捕获前的前台窗口，兜底链结束后无论成功/失败都恢复，
                // 避免"截完图被截窗口一直压在最上方"改变用户的窗口布局。
                var previousForeground = ScreenNativeMethods.GetForegroundWindow();
                WindowForeground.PrepareForRestore();
                using (WindowForeground.SuspendOwnTopmostWindows())
                {
                    try
                    {
                        var activated = WindowForeground.TryActivate(windowHandle);

                        // 激活后重绘常能让 WGC 拿到真帧；即便前台激活受系统限制失败，
                        // 也先试 WGC（被遮挡窗口有时仍可捕获），再决定是否报错。
                        var activatedWgc = WgcCapture.TryCaptureWindow(windowHandle, timeoutMs: 1800);
                        if (activatedWgc is { } activatedFrame && !IsBlank(activatedFrame.Png))
                        {
                            return BuildWindowResult(activatedFrame);
                        }

                        if (!activated || !WindowForeground.IsTargetForeground(windowHandle))
                        {
                            throw new IOException(
                                "无法把目标窗口置于前台，截图会混入其他窗口画面。请手动切换到该窗口后重试。");
                        }

                        var fromScreen = GdiCapture.TryCaptureWindowFromScreen(windowHandle);
                        if (fromScreen is { } screenFrame && !IsBlank(screenFrame.Png))
                        {
                            return BuildWindowResult(screenFrame);
                        }

                        // 各级都判黑：优先返回屏幕裁剪结果（至少是真实桌面内容），
                        // 而不是让用户拿到一张纯黑图或静默失败。
                        var fallback = fromScreen ?? activatedWgc ?? gdi ?? wgc;
                        if (fallback is null)
                        {
                            throw new IOException("窗口截图失败：捕获 API 不可用或窗口已关闭，请重试。");
                        }

                        return BuildWindowResult(fallback.Value);
                    }
                    finally
                    {
                        WindowForeground.RestoreForeground(previousForeground, windowHandle);
                    }
                }
            },
            cancellationToken);

    private static bool IsBlank(byte[] png) => PngFrameInspector.IsBlank(png);

    private static CaptureResult BuildWindowResult((byte[] Png, int Width, int Height) frame)
        => new(
            CaptureType.Window,
            frame.Png,
            frame.Width,
            frame.Height,
            DateTimeOffset.Now);

    public async Task<CaptureResult> CaptureDelayedAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay <= TimeSpan.Zero)
        {
            return await CaptureFullScreenAsync(cancellationToken).ConfigureAwait(true);
        }

        var finished = await Task.Run(
            () => CountdownOverlay.Run(delay, cancellationToken),
            cancellationToken).ConfigureAwait(true);
        if (!finished)
        {
            throw new OperationCanceledException("用户取消了延时截图。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 等倒计时卡片退场，避免卡片残影进入全屏截图。
        await Task.Delay(OverlayTeardownDelay, cancellationToken).ConfigureAwait(true);

        return await CaptureFullScreenAsync(cancellationToken).ConfigureAwait(true);
    }

    private static CaptureResult CaptureRectangle(ScreenNativeMethods.NativeRect rect, CaptureType type)
    {
        if (rect.IsEmpty)
        {
            throw new IOException("截图区域为空，无法生成图像。");
        }

        var png = GdiCapture.CaptureScreenRegionToPng(rect.Left, rect.Top, rect.Width, rect.Height);
        return new CaptureResult(type, png, rect.Width, rect.Height, DateTimeOffset.Now);
    }
}
