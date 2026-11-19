using Lumina.Services.Backup;
using Lumina.Services.Clipboard;
using Lumina.Services.Compatibility;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Notifications;
using Lumina.UI.Wpf.Themes;

namespace Lumina.UI.Wpf.Settings;

/// <summary>设置中心与模块抽屉共享的服务上下文；两级配置读写同一 <see cref="ISettingsStore"/>。</summary>
public sealed class SettingsContext
{
    public SettingsContext(
        ThemeManager themes,
        ISettingsStore settings,
        CompletionStyleHub completionStyles,
        IAutoStartupService autoStartup,
        IWidgetHost widgets,
        IWorldClockService worldClock,
        INoiseMixer noise,
        IHotkeyManager hotkeys,
        HotkeyBindingService hotkeyBindings,
        ClipboardRetentionService clipboardRetention,
        IgnoreRuleService clipboardIgnoreRules,
        IScreenshotStore screenshots,
        INotificationSoundPlayer notificationSound,
        string hotkeyStatus)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(completionStyles);
        ArgumentNullException.ThrowIfNull(autoStartup);
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(worldClock);
        ArgumentNullException.ThrowIfNull(noise);
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(hotkeyBindings);
        ArgumentNullException.ThrowIfNull(clipboardRetention);
        ArgumentNullException.ThrowIfNull(clipboardIgnoreRules);
        ArgumentNullException.ThrowIfNull(screenshots);
        ArgumentNullException.ThrowIfNull(notificationSound);
        ArgumentNullException.ThrowIfNull(hotkeyStatus);

        Themes = themes;
        Settings = settings;
        CompletionStyles = completionStyles;
        AutoStartup = autoStartup;
        Widgets = widgets;
        WorldClock = worldClock;
        Noise = noise;
        Hotkeys = hotkeys;
        HotkeyBindings = hotkeyBindings;
        ClipboardRetention = clipboardRetention;
        ClipboardIgnoreRules = clipboardIgnoreRules;
        Screenshots = screenshots;
        NotificationSound = notificationSound;
        HotkeyStatus = hotkeyStatus;
    }

    public ThemeManager Themes { get; }

    public ISettingsStore Settings { get; }

    public CompletionStyleHub CompletionStyles { get; }

    public IAutoStartupService AutoStartup { get; }

    public IWidgetHost Widgets { get; }

    public IWorldClockService WorldClock { get; }

    public INoiseMixer Noise { get; }

    public IHotkeyManager Hotkeys { get; }

    public HotkeyBindingService HotkeyBindings { get; }

    public ClipboardRetentionService ClipboardRetention { get; }

    public IgnoreRuleService ClipboardIgnoreRules { get; }

    /// <summary>截图历史存储：设置页据此读写保留张数并立即执行淘汰。</summary>
    public IScreenshotStore Screenshots { get; }

    /// <summary>到点提示音播放器：设置页用于试听选中的音效。</summary>
    public INotificationSoundPlayer NotificationSound { get; }

    public string HotkeyStatus { get; }

    /// <summary>启动后采集的兼容性快照；探测完成前为 null，设置页显示“探测中”。</summary>
    public CompatibilitySnapshot? Compatibility { get; set; }

    /// <summary>备份导出与恢复协调器；未接入时为 null，设置页不显示备份卡。</summary>
    public IBackupCoordinator? Backup { get; set; }

    /// <summary>重新打开首次运行引导；未接入时为 null。</summary>
    public Action? RequestOnboarding { get; set; }
}
