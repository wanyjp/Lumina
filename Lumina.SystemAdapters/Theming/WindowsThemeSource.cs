using System.Runtime.Versioning;
using System.Security;
using Lumina.Services.Contracts;
using Microsoft.Win32;

namespace Lumina.SystemAdapters.Theming;

/// <summary>
/// Windows 明暗外观来源：读取当前用户“应用模式”并轮询变化。
/// 轮询间隔 1 秒，开销可忽略；发现变化后由 UI 线程负责重建资源字典。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsThemeSource : ISystemThemeSource
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private bool _lastIsDark;
    private bool _disposed;

    public WindowsThemeSource()
    {
        _lastIsDark = ReadIsDarkMode();
        _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public bool IsDarkMode => ReadIsDarkMode();

    public event EventHandler? Changed;

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var isDark = ReadIsDarkMode();
        lock (_gate)
        {
            if (_disposed || isDark == _lastIsDark)
            {
                return;
            }

            _lastIsDark = isDark;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool ReadIsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(ValueName);
            return value is int lightTheme && lightTheme == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }
}
