using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lumina.Services.Vault;

namespace Lumina.SystemAdapters.Vault;

/// <summary>
/// P3-07：用 Windows DPAPI（CurrentUser）给主密码校验哈希再加一层本机账户保护。
/// 只保护校验哈希，不参与字段加密，因此库文件被拷走也无法在原账户之外完成校验。
/// </summary>
public sealed class DpapiVaultSecretProtector : IVaultSecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    /// <summary>附加熵：同一账户下不同应用的 DPAPI 密文不能互相解。</summary>
    private static readonly byte[] Entropy = "Lumina.Vault.v1"u8.ToArray();

    public bool IsAvailable => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] data) => Transform(data, protect: true);

    public byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 仅在 Windows 上可用。");
        }

        var input = Alloc(data);
        var entropy = Alloc(Entropy);
        var output = default(DataBlob);
        var description = IntPtr.Zero;
        try
        {
            var ok = protect
                ? CryptProtectData(
                    ref input,
                    "Lumina vault",
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    out description,
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!ok)
            {
                throw new CryptographicException(
                    "DPAPI 调用失败。",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            var result = new byte[output.Size];
            if (output.Size > 0)
            {
                Marshal.Copy(output.Data, result, 0, output.Size);
            }

            return result;
        }
        finally
        {
            Free(ref input);
            Free(ref entropy);
            Free(ref output);
            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    private static DataBlob Alloc(byte[] data)
    {
        var blob = new DataBlob
        {
            Size = data.Length,
            Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)),
        };

        if (data.Length > 0)
        {
            Marshal.Copy(data, 0, blob.Data, data.Length);
        }

        return blob;
    }

    private static void Free(ref DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            LocalFree(blob.Data);
        }

        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
