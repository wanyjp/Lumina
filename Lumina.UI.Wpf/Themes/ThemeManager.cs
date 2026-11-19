using System.ComponentModel;
using System.Windows;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Theming;

namespace Lumina.UI.Wpf.Themes;

/// <summary>主题变更事件，供视图更新选择态与状态文本。</summary>
public sealed class ThemeChangedEventArgs : EventArgs
{
    public ThemeChangedEventArgs(ThemeResolution resolution, bool isHighContrast, bool isReducedMotion)
    {
        RequestedTheme = resolution.RequestedTheme;
        EffectiveTheme = resolution.EffectiveTheme;
        IsDark = resolution.IsDark;
        IsHighContrast = isHighContrast;
        IsReducedMotion = isReducedMotion;
    }

    public string RequestedTheme { get; }

    public string EffectiveTheme { get; }

    public bool IsDark { get; }

    public bool IsHighContrast { get; }

    public bool IsReducedMotion { get; }
}

/// <summary>
/// 主题生命周期：读写用户偏好、监听系统明暗、生成并替换应用级资源字典。
/// “跟随系统”仅在用户未手动选档时生效；手动选档后忽略系统变化，再选回“跟随系统”恢复。
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly ISystemThemeSource _systemThemeSource;

    private ResourceDictionary? _themeDictionary;
    private ThemeResolution _resolution = ThemeCatalog.Resolve(ThemeCatalog.Auto, systemIsDark: false);
    private bool _disposed;

    public ThemeManager(ISettingsStore settings, ISystemThemeSource systemThemeSource)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(systemThemeSource);

        _settings = settings;
        _systemThemeSource = systemThemeSource;
        _systemThemeSource.Changed += OnSystemThemeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public event EventHandler<ThemeChangedEventArgs>? Changed;

    public string RequestedTheme => _resolution.RequestedTheme;

    public string EffectiveTheme => _resolution.EffectiveTheme;

    public bool IsDark => _resolution.IsDark;

    public bool IsHighContrast { get; private set; }

    public bool IsReducedMotion { get; private set; }

    /// <summary>读取持久化档位并应用；启动时在创建窗口前调用。</summary>
    public void Initialize()
    {
        ThrowIfDisposed();

        var saved = _settings.Get(PreferenceKeys.Theme, PreferenceDefaults.Theme);
        Apply(ThemeCatalog.Normalize(saved), notify: false);
    }

    /// <summary>切换档位并写入配置；写入即生效。</summary>
    public void SetTheme(string themeId)
    {
        ThrowIfDisposed();

        var normalized = ThemeCatalog.Normalize(themeId);
        _settings.Set(PreferenceKeys.Theme, normalized, module: "settings");
        Apply(normalized, notify: true);
    }

    private void Apply(string requestedTheme, bool notify)
    {
        var resolution = ThemeCatalog.Resolve(requestedTheme, _systemThemeSource.IsDarkMode);
        var highContrast = SystemParameters.HighContrast;
        var reduceMotion = !SystemParameters.ClientAreaAnimation;
        var dictionary = ThemeResourceBuilder.Build(resolution, highContrast, reduceMotion);

        var application = Application.Current;
        if (application is not null)
        {
            var dictionaries = application.Resources.MergedDictionaries;
            if (_themeDictionary is not null)
            {
                dictionaries.Remove(_themeDictionary);
            }

            dictionaries.Add(dictionary);
        }

        _themeDictionary = dictionary;
        _resolution = resolution;
        IsHighContrast = highContrast;
        IsReducedMotion = reduceMotion;

        if (notify)
        {
            Changed?.Invoke(this, new ThemeChangedEventArgs(resolution, highContrast, reduceMotion));
        }
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed || RequestedTheme != ThemeCatalog.Auto)
        {
            return;
        }

        RunOnUiThread(() => Apply(RequestedTheme, notify: true));
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        RunOnUiThread(() => Apply(RequestedTheme, notify: true));
    }

    private static void RunOnUiThread(Action action)
    {
        var application = Application.Current;
        if (application is null)
        {
            action();
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            application.Dispatcher.BeginInvoke(action);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _systemThemeSource.Changed -= OnSystemThemeChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        var application = Application.Current;
        if (application is not null && _themeDictionary is not null)
        {
            application.Resources.MergedDictionaries.Remove(_themeDictionary);
        }

        _themeDictionary = null;
    }
}
