using System.Runtime.Versioning;
using System.Security;
using Lumina.Services.Contracts;
using Microsoft.Win32;

namespace Lumina.SystemAdapters.Startup;

/// <summary>
/// 开机自启：写入当前用户 Run 键，读取时以系统实际值为准。
/// 任何写入失败都抛出可读异常，由设置页/托盘给出可见提示，不静默伪装成功。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryAutoStartupService : IAutoStartupService
{
    public const string ValueName = "Lumina";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var raw = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var storedPath = raw.Trim().Trim('"');
        return string.Equals(storedPath, ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开当前用户启动项注册表键。");

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new InvalidOperationException("无法修改开机自启项，请检查当前用户权限。", ex);
        }
    }

    private static string ExecutablePath =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("无法确定当前程序路径，开机自启不可用。");
}
