using System.Runtime.InteropServices;

namespace Lumina.App.Shell;

/// <summary>进程令牌的完整性级别。Windows 的任务栏只接受 Medium 进程注册的托盘图标。</summary>
internal enum ProcessIntegrityLevel
{
    Unknown,
    Untrusted,
    Low,
    Medium,
    High,
    System,
    Protected,
}

/// <summary>
/// 读取当前进程的完整性级别。两种非 Medium 情况都会让 <c>Shell_NotifyIcon</c> 返回
/// <c>ACCESS_DENIED</c>：被低完整性目录标签降权的 Low 进程，以及以管理员身份启动的 High 进程。
/// 启动时据此给出可执行的修复提示，而不是让托盘静默消失。
/// </summary>
internal static class ProcessIntegrity
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public static ProcessIntegrityLevel Query()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            return ProcessIntegrityLevel.Unknown;
        }

        try
        {
            // 第一次调用只用于取所需缓冲区大小，返回 FALSE + ERROR_INSUFFICIENT_BUFFER 属正常。
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            if (size == 0)
            {
                return ProcessIntegrityLevel.Unknown;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                {
                    return ProcessIntegrityLevel.Unknown;
                }

                var sid = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer).Sid;
                if (sid == IntPtr.Zero)
                {
                    return ProcessIntegrityLevel.Unknown;
                }

                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0)
                {
                    return ProcessIntegrityLevel.Unknown;
                }

                var rid = (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                return rid switch
                {
                    0x0000 => ProcessIntegrityLevel.Untrusted,
                    0x1000 => ProcessIntegrityLevel.Low,
                    0x2000 => ProcessIntegrityLevel.Medium,
                    0x3000 => ProcessIntegrityLevel.High,
                    0x4000 => ProcessIntegrityLevel.System,
                    >= 0x5000 => ProcessIntegrityLevel.Protected,
                    _ => ProcessIntegrityLevel.Unknown,
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static string Describe(ProcessIntegrityLevel level) => level switch
    {
        ProcessIntegrityLevel.Untrusted => "不受信任（Untrusted）",
        ProcessIntegrityLevel.Low => "低完整性（Low）",
        ProcessIntegrityLevel.Medium => "标准用户（Medium）",
        ProcessIntegrityLevel.High => "管理员（High）",
        ProcessIntegrityLevel.System => "系统（System）",
        ProcessIntegrityLevel.Protected => "受保护（Protected）",
        _ => "未知",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public IntPtr Sid;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int informationClass,
        IntPtr information,
        uint informationLength,
        out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
