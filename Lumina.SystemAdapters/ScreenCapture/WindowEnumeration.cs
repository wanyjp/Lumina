using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Services.Contracts;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 可捕获窗口枚举：可见、有标题、无属主、非工具窗、未最小化、未被
/// DWM 遮蔽（cloaked），且不属于本进程。图标转 PNG 字节供选择器展示。
/// </summary>
public static class WindowEnumeration
{
    /// <summary>枚举当前会话的可捕获窗口，按 Z 序（前台在前）返回。</summary>
    public static IReadOnlyList<CapturableWindow> Enumerate()
    {
        var results = new List<CapturableWindow>();
        var selfPid = (uint)Environment.ProcessId;

        var processNames = new Dictionary<int, string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    processNames.TryAdd(process.Id, process.ProcessName);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // 进程枚举失败时窗口标题仍可用，进程名列空。
        }

        _ = ScreenNativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                CollectWindow(hwnd, selfPid, processNames, results);
                return true;
            },
            IntPtr.Zero);

        return results;
    }

    private static void CollectWindow(
        nint hwnd,
        uint selfPid,
        IReadOnlyDictionary<int, string> processNames,
        List<CapturableWindow> results)
    {
        if (!ScreenNativeMethods.IsWindowVisible(hwnd) || ScreenNativeMethods.IsIconic(hwnd))
        {
            return;
        }

        var extendedStyle = ScreenNativeMethods.GetWindowLong(hwnd, ScreenNativeMethods.GwlExStyle);
        if ((extendedStyle & ScreenNativeMethods.WsExToolWindow) != 0)
        {
            return;
        }

        // 有属主的窗口是对话框/悬浮工具，不进入窗口截图选择器。
        if (ScreenNativeMethods.GetWindowLong(hwnd, ScreenNativeMethods.GwlHwndParent) != 0)
        {
            return;
        }

        if (ScreenNativeMethods.DwmGetWindowAttributeInt(
                hwnd,
                ScreenNativeMethods.DwmwaCloaked,
                out var cloaked,
                sizeof(int)) == 0
            && cloaked != 0)
        {
            return;
        }

        if (ScreenNativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == selfPid)
        {
            return;
        }

        var title = ReadWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        if (ScreenNativeMethods.DwmGetWindowAttribute(
                hwnd,
                ScreenNativeMethods.DwmwaExtendedFrameBounds,
                out var bounds,
                Marshal.SizeOf<ScreenNativeMethods.NativeRect>()) != 0
            || bounds.Width < 120
            || bounds.Height < 80)
        {
            return;
        }

        processNames.TryGetValue((int)pid, out var processName);
        results.Add(new CapturableWindow(hwnd, title, processName ?? string.Empty, TryReadIcon(hwnd)));
    }

    private static string ReadWindowTitle(nint hwnd)
    {
        var length = ScreenNativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = ScreenNativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static byte[]? TryReadIcon(nint hwnd)
    {
        try
        {
            var icon = QueryIcon(hwnd);
            if (icon == IntPtr.Zero)
            {
                return null;
            }

            using var converted = Icon.FromHandle(icon);
            using var bitmap = converted.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }

    private static IntPtr QueryIcon(nint hwnd)
    {
        // ICON_SMALL2（2）质量最好，失败依次回落小图标与类图标；
        // 类图标为共享资源，只读取不销毁。
        if (ScreenNativeMethods.SendMessageTimeout(
                hwnd,
                ScreenNativeMethods.WmGetIcon,
                new IntPtr(2),
                IntPtr.Zero,
                0x0002, // SMTO_ABORTIFHUNG
                300,
                out var small2) != IntPtr.Zero
            && small2 != IntPtr.Zero)
        {
            return small2;
        }

        if (ScreenNativeMethods.SendMessageTimeout(
                hwnd,
                ScreenNativeMethods.WmGetIcon,
                IntPtr.Zero,
                IntPtr.Zero,
                0x0002,
                300,
                out var small) != IntPtr.Zero
            && small != IntPtr.Zero)
        {
            return small;
        }

        return ScreenNativeMethods.GetClassLong(hwnd, -14); // GCL_HICON
    }
}
