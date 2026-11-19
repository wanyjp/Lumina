using System.Runtime.InteropServices;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 窗口截图的前台准备：把目标窗口可靠地置于 Z 序顶端，并临时隐藏本进程的置顶窗口
/// （小组件、贴图），避免"整屏 BitBlt 裁剪"兜底把遮挡物或桌面当成目标窗口内容。
/// 不提升进程权限；依次尝试线程输入附着、ALT 解锁与 Alt+Tab 语义切换三级激活。
/// </summary>
internal static class WindowForeground
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint GaRoot = 2;
    private const long WsExTopmost = 0x00000008L;

    /// <summary>
    /// 激活目标窗口并等待 DWM 合成完成；返回前台是否确为目标窗口（或其子窗口）。
    /// 目标最小化时先还原；失败返回 false，由调用方决定改走错误提示。
    /// </summary>
    public static bool TryActivate(nint windowHandle, int settleMs = 260)
    {
        if (windowHandle == 0 || !ScreenNativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        if (ScreenNativeMethods.IsIconic(windowHandle))
        {
            ScreenNativeMethods.ShowWindow(windowHandle, SwRestore);
        }

        if (IsTargetForeground(windowHandle))
        {
            Thread.Sleep(Math.Min(settleMs, 120));
            return true;
        }

        // 阶段一：附着线程输入后调用 SetForegroundWindow（标准跨进程激活手法）。
        var currentThread = ScreenNativeMethods.GetCurrentThreadId();
        var foregroundThread = ScreenNativeMethods.GetWindowThreadProcessId(
            ScreenNativeMethods.GetForegroundWindow(), out _);
        var targetThread = ScreenNativeMethods.GetWindowThreadProcessId(windowHandle, out _);

        var attachedForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && ScreenNativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0
            && targetThread != currentThread
            && targetThread != foregroundThread
            && ScreenNativeMethods.AttachThreadInput(currentThread, targetThread, true);

        try
        {
            ScreenNativeMethods.ShowWindow(windowHandle, SwShow);
            ScreenNativeMethods.BringWindowToTop(windowHandle);
            ScreenNativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedTarget)
            {
                ScreenNativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                ScreenNativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        if (WaitForForeground(windowHandle, Math.Max(80, settleMs / 2)))
        {
            return true;
        }

        // 阶段二：模拟一次 ALT 按键解除前台锁定，再激活。
        try
        {
            ScreenNativeMethods.UnlockForeground();
            ScreenNativeMethods.SetForegroundWindow(windowHandle);
            if (WaitForForeground(windowHandle, Math.Max(80, settleMs / 2)))
            {
                return true;
            }

            // 阶段三：SwitchToThisWindow 走与 Alt+Tab 相同的切换语义，
            // 对部分受保护/多进程宿主窗口比 SetForegroundWindow 更有效。
            ScreenNativeMethods.SwitchToThisWindow(windowHandle, true);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // 极旧系统缺少该导出：保持已有结果。
        }

        return WaitForForeground(windowHandle, Math.Max(80, settleMs));
    }

    private static bool WaitForForeground(nint windowHandle, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (IsTargetForeground(windowHandle))
            {
                return true;
            }

            Thread.Sleep(30);
        }

        return IsTargetForeground(windowHandle);
    }

    /// <summary>
    /// 截图前调用：本进程此刻仍是前台进程，预先解除前台锁定，
    /// 使截图结束后（本进程已变回后台进程）仍能把焦点还给原窗口。
    /// </summary>
    public static void PrepareForRestore()
        => ScreenNativeMethods.AllowSetForegroundWindow(ScreenNativeMethods.AsfwAny);

    /// <summary>
    /// 截图结束后恢复捕获前的前台窗口。仅在确实切换过前台时生效，
    /// 不触碰用户此后主动切换的窗口，也不恢复已关闭的句柄。
    /// </summary>
    public static void RestoreForeground(nint previousForeground, nint capturedWindow)
    {
        if (previousForeground == IntPtr.Zero
            || previousForeground == capturedWindow
            || !ScreenNativeMethods.IsWindow(previousForeground)
            || IsTargetForeground(previousForeground)
            || !IsTargetForeground(capturedWindow))
        {
            // 前台已不是被截窗口：说明用户在捕获期间主动切换过，不再抢回焦点。
            return;
        }

        ScreenNativeMethods.ShowWindow(previousForeground, SwShow);
        ScreenNativeMethods.BringWindowToTop(previousForeground);
        ScreenNativeMethods.SetForegroundWindow(previousForeground);

        if (!TryActivate(previousForeground, settleMs: 320))
        {
            // 焦点仍无法回退时，至少把被截窗口从 Z 序顶端降下来，
            // 避免它一直压在用户原本使用的窗口之上。
            ScreenNativeMethods.SwitchToThisWindow(previousForeground, true);
        }
    }

    /// <summary>目标窗口是否处于前台；弹出子窗口归属同一根窗口时也算命中。</summary>
    public static bool IsTargetForeground(nint windowHandle)
    {
        var foreground = ScreenNativeMethods.GetForegroundWindow();
        return foreground == windowHandle
            || (foreground != IntPtr.Zero
                && ScreenNativeMethods.GetAncestor(foreground, GaRoot) == windowHandle);
    }

    /// <summary>临时隐藏本进程的可见置顶窗口，返回可恢复的作用域。</summary>
    public static IDisposable SuspendOwnTopmostWindows()
    {
        var selfPid = (uint)Environment.ProcessId;
        var hidden = new List<nint>();

        try
        {
            _ = ScreenNativeMethods.EnumWindows(
                (hwnd, _) =>
                {
                    if (!ScreenNativeMethods.IsWindowVisible(hwnd))
                    {
                        return true;
                    }

                    var thread = ScreenNativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                    if (thread == 0 || pid != selfPid)
                    {
                        return true;
                    }

                    var style = ScreenNativeMethods.GetWindowLong(hwnd, ScreenNativeMethods.GwlExStyle);
                    if ((style & WsExTopmost) == 0)
                    {
                        return true;
                    }

                    if (ScreenNativeMethods.ShowWindow(hwnd, SwHide))
                    {
                        hidden.Add(hwnd);
                    }

                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            // 枚举失败不阻断截图：直接返回空作用域。
        }

        return new TopmostSuspension(hidden);
    }

    private sealed class TopmostSuspension(IReadOnlyList<nint> hidden) : IDisposable
    {
        private bool _restored;

        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            foreach (var hwnd in hidden)
            {
                if (ScreenNativeMethods.IsWindow(hwnd))
                {
                    ScreenNativeMethods.ShowWindow(hwnd, SwShow);
                }
            }
        }
    }
}
