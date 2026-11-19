using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Theming;
using Lumina.UI.Wpf.Themes;

namespace Lumina.UI.Wpf.Onboarding;

/// <summary>
/// 首次运行引导：主题、开机自启、剪贴板采集（含隐私说明）与主密码入口。
/// 非模态显示，主窗口始终可用；关闭窗口（含“稍后设置”）即视为完成，可随时从设置重开。
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly ThemeManager _themes;
    private readonly IAutoStartupService _autoStartup;
    private readonly ISettingsStore _settings;
    private bool _initializing = true;

    public FirstRunWindow(
        Window owner,
        ThemeManager themes,
        IAutoStartupService autoStartup,
        ISettingsStore settings,
        IVaultService vault,
        string? dataRootNotice)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(autoStartup);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(vault);

        InitializeComponent();

        Owner = owner;
        _themes = themes;
        _autoStartup = autoStartup;
        _settings = settings;

        ThemeBox.ItemsSource = ThemeCatalog.All
            .Select(id => new ThemeChoice(id, ThemeCatalog.DisplayName(id)))
            .ToList();
        ThemeBox.DisplayMemberPath = nameof(ThemeChoice.Label);
        ThemeBox.SelectedValuePath = nameof(ThemeChoice.Id);
        ThemeBox.SelectedValue = themes.RequestedTheme;

        AutoStartToggle.IsChecked = ReadAutoStart();
        ClipboardToggle.IsChecked = settings.Get(
            PreferenceKeys.ClipboardCaptureEnabled,
            PreferenceDefaults.ClipboardCaptureEnabled);

        VaultStatusText.Text = vault.IsConfigured
            ? (vault.IsLocked ? "已设置主密码，当前已锁定。" : "已设置主密码。")
            : "尚未设置主密码。";
        SetupVaultButton.Visibility = vault.IsConfigured
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(dataRootNotice))
        {
            DataRootNoticeText.Text = dataRootNotice;
            DataRootNoticeCard.Visibility = Visibility.Visible;
        }

        _initializing = false;
    }

    /// <summary>用户点击“现在设置主密码”；关闭后由宿主导航到密码管理器。</summary>
    public bool OpenVaultRequested { get; private set; }

    private bool ReadAutoStart()
    {
        try
        {
            var enabled = _autoStartup.IsEnabled();
            AutoStartHint.Text = enabled ? "已加入当前用户启动项。" : string.Empty;
            return enabled;
        }
        catch (Exception ex)
        {
            AutoStartHint.Text = $"无法读取开机自启状态：{ex.Message}";
            return false;
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        if (ThemeBox.SelectedValue is string themeId)
        {
            _themes.SetTheme(themeId);
        }
    }

    private void OnAutoStartClick(object sender, RoutedEventArgs e)
    {
        var desired = AutoStartToggle.IsChecked == true;
        try
        {
            _autoStartup.SetEnabled(desired);
            AutoStartHint.Text = desired ? "已加入当前用户启动项。" : "已从启动项移除。";
        }
        catch (Exception ex)
        {
            AutoStartToggle.IsChecked = !desired;
            AutoStartHint.Text = $"设置失败：{ex.Message}";
        }
    }

    private void OnClipboardToggleClick(object sender, RoutedEventArgs e)
    {
        var enabled = ClipboardToggle.IsChecked == true;
        try
        {
            _settings.Set(
                PreferenceKeys.ClipboardCaptureEnabled,
                enabled,
                module: "onboarding");
            ClipboardHint.Text = enabled ? "已启用，可在设置中心随时暂停或关闭。" : "已关闭，不会记录新的剪贴板内容。";
        }
        catch (Exception ex)
        {
            ClipboardToggle.IsChecked = !enabled;
            ClipboardHint.Text = $"保存失败：{ex.Message}";
        }
    }

    private void OnSetupVaultClick(object sender, RoutedEventArgs e)
    {
        OpenVaultRequested = true;
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => Close();

    private void OnFinishClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _settings.Set(PreferenceKeys.OnboardingCompleted, true, module: "onboarding");
        }
        catch (Exception)
        {
            // 配置写入失败不应阻止窗口关闭；下次启动会再次显示引导。
        }

        base.OnClosed(e);
    }

    private sealed record ThemeChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
