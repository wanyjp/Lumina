using System.Runtime.InteropServices;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 截图捕获共用的 Win32 声明：GDI 位图拷贝、虚拟屏与监视器枚举、
/// 分层窗口合成、光标与窗口信息。全部使用物理像素坐标。
/// </summary>
internal static class ScreenNativeMethods
{
    // ---- 消息常量 ----
    public const int WmKeyDown = 0x0100;
    public const int WmMouseMove = 0x0200;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonUp = 0x0205;
    public const int WmCaptureChanged = 0x0215;

    public const int VkEscape = 0x1B;
    public const int VkReturn = 0x0D;
    public const int VkLButton = 0x01;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>鼠标左键当前是否按下（用于松开检测的消息兜底）。</summary>
    public static bool IsLeftButtonDown() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    // ---- GDI ----
    public const int Srccopy = 0x00CC0020;
    public const int CaptureBlt = 0x40000000;
    public const int PrintWindowRenderFullContent = 0x00000002;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(
        IntPtr hdcDestination,
        int xDestination,
        int yDestination,
        int width,
        int height,
        IntPtr hdcSource,
        int xSource,
        int ySource,
        int rop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    // ---- 光标 ----
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    // ---- 监视器 ----
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    // ---- 分层窗口 ----
    public const uint UlwAlpha = 2;
    public const byte AcSrcOver = 0;
    public const byte AcSrcAlpha = 1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDestination,
        ref NativePoint destinationPosition,
        ref NativeSize size,
        IntPtr hdcSource,
        ref NativePoint sourcePosition,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    // ---- 窗口信息 ----
    public const int GwlExStyle = -20;
    public const int GwlHwndParent = -8;
    public const long WsExToolWindow = 0x00000080L;
    public const int DwmwaExtendedFrameBounds = 9;
    public const int DwmwaCloaked = 14;
    public const int WmGetIcon = 0x007F;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hwnd, IntPtr rectUpdate, IntPtr regionUpdate, uint flags);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    /// <summary>ASFW_ANY：允许任意进程随后调用 SetForegroundWindow。</summary>
    public const uint AsfwAny = uint.MaxValue;

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hwnd, bool useAltTabOrder);

    /// <summary>
    /// 前台锁定解锁：模拟一次 ALT 按下/抬起，使后续 SetForegroundWindow 被系统允许。
    /// 这是 SetForegroundWindow 文档记载的常规解锁手法，不修改任何系统配置。
    /// </summary>
    public static void UnlockForeground()
    {
        const byte VkMenu = 0x12;
        const uint KeyeventfKeyup = 0x0002;
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    /// <summary>触发目标窗口立即重绘，避免 WGC 因窗口静止而迟迟不产生新帧。</summary>
    public static void ForceWindowRedraw(IntPtr hwnd)
    {
        // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100);
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern int GetClassLong32(IntPtr hwnd, int index);

    public static IntPtr GetClassLong(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetClassLongPtr64(hwnd, index) : new IntPtr(GetClassLong32(hwnd, index));

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public readonly bool IsEmpty => Width <= 0 || Height <= 0;
    }

    /// <summary>虚拟屏（所有显示器并集）物理像素矩形，支持负坐标。</summary>
    public static NativeRect VirtualScreen()
        => new(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmXVirtualScreen) + GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen) + GetSystemMetrics(SmCyVirtualScreen));

    /// <summary>枚举所有显示器的物理像素矩形。</summary>
    public static List<NativeRect> EnumMonitorRects()
    {
        var rects = new List<NativeRect>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, ref rect, _) =>
            {
                rects.Add(rect);
                return true;
            },
            IntPtr.Zero);
        return rects;
    }
}
