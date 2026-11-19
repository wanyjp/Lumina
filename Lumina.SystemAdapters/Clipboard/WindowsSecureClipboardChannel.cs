using System.Runtime.InteropServices;
using System.Text;
using Lumina.Services.Clipboard;
using Lumina.Services.Contracts;
using Lumina.Services.Security;
using Microsoft.Extensions.Logging;

namespace Lumina.SystemAdapters.Clipboard;

/// <summary>
/// P3-09 安全剪贴板通道：直接走 Win32，把文本写成 CF_UNICODETEXT，
/// 同时附加 CanIncludeInClipboardHistory=0 / CanUploadToCloudClipboard=0 /
/// ExcludeClipboardContentFromMonitorProcessing，使 Windows 历史与云同步都不采集。
/// 读取只回哈希；所有操作在 STA 线程执行，避免剪贴板 API 的线程模型限制。
/// </summary>
public sealed class WindowsSecureClipboardChannel : ISecureClipboardChannel
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int OpenRetries = 6;

    /// <summary>DWORD 0：Windows 只在值为 0 时排除历史/云同步。</summary>
    private static readonly byte[] ExcludeDword = new byte[4];

    private readonly IClipboardMonitor _monitor;
    private readonly ILogger<WindowsSecureClipboardChannel>? _logger;

    public WindowsSecureClipboardChannel(
        IClipboardMonitor monitor,
        ILogger<WindowsSecureClipboardChannel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
        _logger = logger;
    }

    public bool TryWriteSecret(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // 先请求监听器跳过本次变更，再写入；排除格式是第二道保险。
        _monitor.SuppressCapture();
        var written = RunSta(() => Write(text));
        if (!written)
        {
            _logger?.LogWarning("安全剪贴板写入失败（可能被其他进程占用）。");
        }

        return written;
    }

    public string? TryReadTextHash() => RunSta(ReadHash);

    public bool TryClear() => RunSta(Clear);

    private static bool Write(string text)
    {
        if (!OpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            var payload = Encoding.Unicode.GetBytes(text + "\0");
            var formats = new List<(uint Format, byte[] Bytes)> { (CfUnicodeText, payload) };
            foreach (var name in new[]
                     {
                         ClipboardPrivacyFilter.CanIncludeInHistoryFormat,
                         ClipboardPrivacyFilter.CanUploadToCloudFormat,
                         ClipboardPrivacyFilter.ExcludeFromMonitorFormat,
                     })
            {
                var format = NativeMethods.RegisterClipboardFormat(name);
                if (format != 0)
                {
                    formats.Add((format, ExcludeDword));
                }
            }

            // 先备好全部 HGLOBAL：任何分配失败都发生在清空剪贴板之前，原内容不受影响。
            var handles = new List<(uint Format, IntPtr Handle)>(formats.Count);
            foreach (var (format, bytes) in formats)
            {
                var handle = AllocBytes(bytes);
                if (handle == IntPtr.Zero)
                {
                    FreeHandles(handles);
                    return false;
                }

                handles.Add((format, handle));
            }

            if (!NativeMethods.EmptyClipboard())
            {
                FreeHandles(handles);
                return false;
            }

            var textWritten = false;
            foreach (var (format, handle) in handles)
            {
                if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(handle);
                    continue;
                }

                // 成功后内存所有权移交剪贴板，不能再释放。
                if (format == CfUnicodeText)
                {
                    textWritten = true;
                }
            }

            return textWritten;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static IntPtr AllocBytes(byte[] bytes)
    {
        var handle = NativeMethods.GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var target = NativeMethods.GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(bytes, 0, target, bytes.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }

    private static void FreeHandles(List<(uint Format, IntPtr Handle)> handles)
    {
        foreach (var (_, handle) in handles)
        {
            NativeMethods.GlobalFree(handle);
        }
    }

    private static string? ReadHash()
    {
        if (!OpenClipboardWithRetry())
        {
            return null;
        }

        try
        {
            // 能打开但没文本（图片/文件）：返回空串，由调用方判定为“已被覆盖”。
            if (!NativeMethods.IsClipboardFormatAvailable(CfUnicodeText))
            {
                return string.Empty;
            }

            var handle = NativeMethods.GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var text = Marshal.PtrToStringUni(pointer);
                return string.IsNullOrEmpty(text)
                    ? string.Empty
                    : SecureClipboardBroker.ComputeContentHash(text);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool Clear()
    {
        if (!OpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            return NativeMethods.EmptyClipboard();
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < OpenRetries; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static bool RunSta(Func<bool> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        var result = false;
        var thread = new Thread(() => result = action())
        {
            IsBackground = true,
            Name = "Lumina.SecureClipboard",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(3)) && result;
    }

    private static string? RunSta(Func<string?> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return action();
        }

        string? result = null;
        var thread = new Thread(() => result = action())
        {
            IsBackground = true,
            Name = "Lumina.SecureClipboard",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(3)) ? result : null;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint format, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterClipboardFormat(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr handle);
    }
}
