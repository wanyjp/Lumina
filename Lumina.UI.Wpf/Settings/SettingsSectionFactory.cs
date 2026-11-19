using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lumina.Domain.Enums;
using Lumina.Services.Clipboard;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Notifications;
using Lumina.Services.Theming;
using Lumina.Services.Widgets;
using Lumina.UI.Wpf.Clock;

namespace Lumina.UI.Wpf.Settings;

/// <summary>
/// 设置分组的统一构建器：设置中心与模块抽屉都从这里取内容，
/// 保证同一偏好只有一处读写口径。
/// </summary>
public static class SettingsSectionFactory
{
    private static readonly (string Key, string Label)[] ClockFormatOptions =
    [
        ("24", "24 小时制"),
        ("12", "12 小时制"),
    ];

    private static readonly (string Key, string Label)[] PeriodModeOptions =
    [
        ("modern", "现代时段（上午 / 下午）"),
        ("ancient", "十二时辰"),
        ("none", "不显示时段"),
    ];

    private static readonly (string Key, string Label)[] CalendarLayoutOptions =
    [
        ("horizontal", "横版（日历与安排左右并列）"),
        ("vertical", "竖版（日历全宽，安排纵向排列）"),
    ];

    private static readonly (string Key, string Label)[] WeekStartOptions =
    [
        ("1", "周一"),
        ("0", "周日"),
    ];

    private static readonly (string Key, string Label)[] ReminderOptions =
    [
        ("none", "不提醒"),
        ("ontime", "准时"),
        ("m5", "提前 5 分钟"),
        ("m15", "提前 15 分钟"),
        ("m30", "提前 30 分钟"),
        ("h1", "提前 1 小时"),
        ("d1", "提前 1 天"),
    ];

    public static FrameworkElement BuildGroup(SettingsContext context, string groupId) =>
        groupId switch
        {
            SettingsGroupCatalog.General => BuildGeneral(context),
            SettingsGroupCatalog.Appearance => BuildAppearance(context),
            SettingsGroupCatalog.Hotkeys => new HotkeySettingsView(context),
            SettingsGroupCatalog.Clock => BuildClock(context),
            SettingsGroupCatalog.Schedule => BuildSchedule(context),
            SettingsGroupCatalog.Reminders => BuildReminders(context),
            SettingsGroupCatalog.Clipboard => BuildClipboard(context),
            SettingsGroupCatalog.Capture => BuildCaptureInfo(context),
            SettingsGroupCatalog.Countdown => BuildCountdown(context),
            SettingsGroupCatalog.Focus => BuildFocusInfo(context),
            SettingsGroupCatalog.Note => BuildNoteInfo(context),
            SettingsGroupCatalog.Vault => BuildVaultInfo(context),
            SettingsGroupCatalog.About => BuildAbout(context),
            _ => BuildGeneral(context),
        };

    /// <summary>模块抽屉内容：只构建当前模块需要的设置。</summary>
    public static FrameworkElement BuildModule(
        SettingsContext context,
        string moduleId,
        string moduleTitle)
    {
        return moduleId switch
        {
            "clock" => BuildClock(context),
            "schedule" => BuildSchedule(context),
            "countdown" => BuildCountdown(context),
            "todo" => BuildTodo(context),
            "clipboard" => BuildClipboard(context),
            "capture" => BuildCaptureInfo(context, isModule: true),
            "focus" => BuildFocusInfo(context),
            "note" => BuildNoteInfo(context),
            "vault" => BuildVaultInfo(context),
            _ => BuildModuleBoundary(context, moduleId, moduleTitle),
        };
    }

    private static StackPanel BuildGeneral(SettingsContext context)
    {
        var panel = Panel();
        panel.Children.Add(BuildAutoStartCard(context));
        panel.Children.Add(BuildGlassCaptureCard(context));

        return panel;
    }

    /// <summary>
    /// 小组件玻璃层的实时捕获开关（实验）：开启前用二次确认把代价讲清楚，
    /// 取消时开关回到关闭状态，不写入偏好。
    /// </summary>
    private static Border BuildGlassCaptureCard(SettingsContext context)
    {
        var enabled = context.Settings.Get(
            PreferenceKeys.WidgetGlassCapture,
            PreferenceDefaults.WidgetGlassCapture);

        var toggle = Toggle("逐帧捕获（实验）", enabled, null);
        AutomationProperties.SetName(toggle, "逐帧捕获");

        var status = Hint(string.Empty);

        // 捕获起不来时，在开关简介下面用红字说明退回采样模式的原因。
        var fallback = Hint(string.Empty);
        fallback.Foreground = Brush("Theme.DangerBrush");
        fallback.Visibility = Visibility.Collapsed;

        void UpdateStatus()
        {
            var on = toggle.IsChecked == true;
            status.Text = on
                ? "已开启：玻璃背景由系统逐帧推送；屏幕录制、直播与远程协助里看不到这些组件。"
                : "关闭时使用 0.2 秒采样，检测到鼠标/窗口/画面变化时自动升到约 30 帧。";

            var reason = on ? WidgetGlassCaptureStatus.FallbackReason : null;
            fallback.Text = reason is null ? string.Empty : $"已回退到 0.2 秒采样：{reason}";
            fallback.Visibility = reason is null ? Visibility.Collapsed : Visibility.Visible;
        }

        void OnCaptureStatusChanged(object? sender, EventArgs e)
        {
            if (!toggle.Dispatcher.CheckAccess())
            {
                toggle.Dispatcher.BeginInvoke(new Action(UpdateStatus));
                return;
            }

            UpdateStatus();
        }

        // 设置页可能被缓存复用，按进入/离开重新挂事件，避免再次打开时看不到回退原因。
        var subscribed = false;
        void Subscribe()
        {
            UpdateStatus();
            if (subscribed)
            {
                return;
            }

            WidgetGlassCaptureStatus.Changed += OnCaptureStatusChanged;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            WidgetGlassCaptureStatus.Changed -= OnCaptureStatusChanged;
            subscribed = false;
        }

        toggle.Click += (_, _) =>
        {
            if (toggle.IsChecked != true)
            {
                context.Settings.Set(PreferenceKeys.WidgetGlassCapture, false, module: "widget");
                WidgetGlassCaptureStatus.Reset();
                UpdateStatus();
                return;
            }

            var owner = Window.GetWindow(toggle);
            var message = string.Join(
                Environment.NewLine,
                "开启后，亚克力与液态玻璃组件改用系统级捕获（DWM / Windows.Graphics.Capture）实时更新背景。",
                string.Empty,
                "会带来的影响：",
                "· 屏幕录制、直播、远程协助中看不到这些小组件；",
                "· 受保护内容（DRM 视频、部分银行控件）会显示为黑色；",
                "· 捕获会话持续占用 GPU，笔记本电池续航会下降；",
                "· 只影响「亚克力 / 液态玻璃」皮肤，其他皮肤不变。",
                string.Empty,
                "点击穿透、拖动与圆角都不受影响。",
                string.Empty,
                "确定开启吗？");

            var result = owner is null
                ? MessageBox.Show(
                    message,
                    "开启逐帧捕获",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel)
                : MessageBox.Show(
                    owner,
                    message,
                    "开启逐帧捕获",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
            {
                toggle.IsChecked = false;
                UpdateStatus();
                return;
            }

            context.Settings.Set(PreferenceKeys.WidgetGlassCapture, true, module: "widget");
            UpdateStatus();
        };

        var stack = Panel();
        stack.Children.Add(Row(
            "逐帧捕获（实验）",
            "用系统级捕获实时更新玻璃背景，更跟手、静止时不耗 CPU；开启后录屏里看不到组件。",
            toggle));
        stack.Children.Add(fallback);
        stack.Children.Add(status);
        stack.Loaded += (_, _) => Subscribe();
        stack.Unloaded += (_, _) => Unsubscribe();
        return Card(
            "小组件玻璃材质",
            "实验特性：默认走低开销采样，逐帧捕获需要显式开启。",
            stack);
    }

    private static StackPanel BuildAppearance(SettingsContext context)
    {
        var panel = Panel();

        var themeStatus = Hint(context.Themes.RequestedTheme);
        themeStatus.Margin = new Thickness(0, 8, 0, 0);

        var updateThemeStatus = new Action(() =>
        {
            themeStatus.Text =
                $"当前：{ThemeCatalog.DisplayName(context.Themes.RequestedTheme)}"
                + $" · 生效：{ThemeCatalog.DisplayName(context.Themes.EffectiveTheme)}";
        });
        updateThemeStatus();

        var themeChoices = Choices(
            ThemeCatalog.All.Select(id => (id, ThemeCatalog.DisplayName(id))).ToArray(),
            context.Themes.RequestedTheme,
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string themeId })
                {
                    context.Themes.SetTheme(themeId);
                    updateThemeStatus();
                }
            },
            "SettingsTheme");

        var themeStack = Panel();
        themeStack.Children.Add(themeChoices);
        themeStack.Children.Add(themeStatus);
        panel.Children.Add(Card(
            "外观主题",
            "跟随系统仅在未手动选档时生效；手动选档后忽略系统明暗变化。",
            themeStack));

        panel.Children.Add(BuildCompletionCard(context));

        return panel;
    }

    private static Border BuildCompletionCard(SettingsContext context)
    {
        var choices = Choices(
            CompletionStyleCatalog.All.Select(strategy =>
                (CompletionStyleCatalog.Key(strategy.Style), CompletionStyleCatalog.Label(strategy.Style)))
                .ToArray(),
            CompletionStyleCatalog.Key(context.CompletionStyles.Current),
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.CompletionStyles.Set(CompletionStyleCatalog.Parse(key));
                }
            },
            "SettingsCompletionStyle");

        var reduce = Toggle("减少", context.CompletionStyles.ReducedMotion, null);
        reduce.Click += (_, _) =>
            context.CompletionStyles.SetReducedMotion(reduce.IsChecked == true);

        var stack = Panel();
        stack.Children.Add(choices);
        stack.Children.Add(Row(
            "减少动画",
            "系统关闭动画或开启此项时，完成切换为瞬时。",
            reduce));
        stack.Children.Add(Hint("切换后待办页与日历“当天待办”即时重渲染。"));

        return Card("完成风格", "四套策略共享同一状态语义，默认描边勾选。", stack);
    }

    private static StackPanel BuildClock(SettingsContext context)
    {
        var panel = Panel();

        var formatChoices = Choices(
            ClockFormatOptions,
            context.Settings.Get<string>(
                PreferenceKeys.ClockFormat,
                PreferenceDefaults.ClockFormat) ?? PreferenceDefaults.ClockFormat,
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.Settings.Set(PreferenceKeys.ClockFormat, key, module: "clock");
                }
            },
            "SettingsClockFormat");

        var periodChoices = Choices(
            PeriodModeOptions,
            context.Settings.Get<string>(
                PreferenceKeys.PeriodMode,
                PreferenceDefaults.PeriodMode) ?? PreferenceDefaults.PeriodMode,
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.Settings.Set(PreferenceKeys.PeriodMode, key, module: "clock");
                }
            },
            "SettingsPeriodMode");

        var displayStack = Panel();
        displayStack.Children.Add(new TextBlock
        {
            Text = "时间制式",
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        });
        displayStack.Children.Add(formatChoices);
        displayStack.Children.Add(new TextBlock
        {
            Text = "时段显示",
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        });
        displayStack.Children.Add(periodChoices);
        panel.Children.Add(Card("时钟显示", "主时钟与时钟小组件同步生效。", displayStack));

        var fullscreen = Toggle(
            "开始番茄后进入全屏",
            context.Settings.Get<bool>(
                PreferenceKeys.PomodoroFullscreen,
                PreferenceDefaults.PomodoroFullscreen),
            null);
        fullscreen.Click += (_, _) =>
            context.Settings.Set(
                PreferenceKeys.PomodoroFullscreen,
                fullscreen.IsChecked == true,
                module: "clock");

        panel.Children.Add(Card(
            "番茄钟",
            "关闭后开始番茄仍停留在计时器页签，可手动放大。",
            fullscreen));

        var compactWidget = Toggle(
            "小组件使用「小」尺寸",
            context.Settings.Get<bool>(
                PreferenceKeys.TimerWidgetCompact,
                PreferenceDefaults.TimerWidgetCompact),
            null);
        compactWidget.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.TimerWidgetCompact,
            compactWidget.IsChecked == true,
            module: "clock");

        var timerWidgetStack = Panel();
        timerWidgetStack.Children.Add(Row(
            "计时器 / 秒表 / 番茄钟小组件",
            "「小」只保留时间与按钮；「大」额外显示状态与提示行。三个小组件互相独立，位置分别记忆。",
            compactWidget));
        timerWidgetStack.Children.Add(Hint(
            "修改后已打开的小组件会立即切换尺寸；可拖动卡片移动，靠近屏幕边缘自动吸附。"));
        panel.Children.Add(Card("计时小组件", "控制缩小为小组件后的密度。", timerWidgetStack));

        panel.Children.Add(BuildModuleWidgetCard(
            context,
            "桌面小组件",
            "四个组件独立记忆位置、置顶、穿透、不透明度与皮肤；关掉置顶后固定在桌面层。",
            (WidgetKind.Clock, "时钟小组件", "大时钟与日期，不承载计时按钮。"),
            (WidgetKind.Timer, "计时器小组件", "倒计时结束提醒；计时页可一键缩小到桌面。"),
            (WidgetKind.Stopwatch, "秒表小组件", "计次与累计，独立于计时器。"),
            (WidgetKind.Pomodoro, "番茄钟小组件", "番茄阶段与剩余时间，跟随专注设置。")));

        panel.Children.Add(Card(
            "世界时钟地区",
            "选择当前地区，或从全球城市目录添加；时钟页右上角也可直接添加城市。",
            new WorldClockSettingsView(context.WorldClock)));

        panel.Children.Add(Card(
            "白噪音",
            "输出设备、总音量与声源组合在此统一管理。",
            new NoiseSettingsView(context.Noise)));

        // 计时器/番茄钟都在时钟模块，提示音就近放在这里，省去跳到设置中心。
        panel.Children.Add(BuildNotificationSoundCard(context));

        return panel;
    }

    private static StackPanel BuildSchedule(SettingsContext context)
    {
        var panel = Panel();

        var layoutChoices = Choices(
            CalendarLayoutOptions,
            context.Settings.Get<string>(
                PreferenceKeys.CalendarLayout,
                PreferenceDefaults.CalendarLayout) ?? PreferenceDefaults.CalendarLayout,
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.Settings.Set(PreferenceKeys.CalendarLayout, key, module: "calendar");
                }
            },
            "SettingsCalendarLayout");

        panel.Children.Add(Card("日历布局", "切换后日历页立即重排，设置随重启保持。", layoutChoices));

        var weekChoices = Choices(
            WeekStartOptions,
            context.Settings.Get<int>(
                PreferenceKeys.WeekStart,
                PreferenceDefaults.WeekStart).ToString(),
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.Settings.Set(
                        PreferenceKeys.WeekStart,
                        key == "1" ? 1 : 0,
                        module: "calendar");
                }
            },
            "SettingsWeekStart");

        panel.Children.Add(Card("每周起始", "影响月历列头与日期排布。", weekChoices));

        panel.Children.Add(BuildModuleWidgetCard(
            context,
            "桌面小组件",
            "今日安排与待办勾选；固定在桌面时不遮挡其它窗口。",
            (WidgetKind.Schedule, "日程小组件", "今日时间轴、优先级与待办勾选。")));

        return panel;
    }

    private static StackPanel BuildReminders(SettingsContext context)
    {
        var panel = Panel();

        panel.Children.Add(BuildReminderCard(
            context,
            "日程默认提醒",
            PreferenceKeys.ScheduleReminder,
            PreferenceDefaults.ScheduleReminder,
            "calendar",
            "新建日程时默认选中的提醒档位；单条日程仍可单独修改。"));

        var inApp = Toggle(
            "应用内提醒条",
            context.Settings.Get<bool>(
                PreferenceKeys.InAppReminder,
                PreferenceDefaults.InAppReminder),
            null);
        inApp.Click += (_, _) =>
            context.Settings.Set(
                PreferenceKeys.InAppReminder,
                inApp.IsChecked == true,
                module: "reminders");

        var system = Toggle(
            "系统通知",
            context.Settings.Get<bool>(
                PreferenceKeys.SystemNotification,
                PreferenceDefaults.SystemNotification),
            null);
        system.Click += (_, _) =>
            context.Settings.Set(
                PreferenceKeys.SystemNotification,
                system.IsChecked == true,
                module: "reminders");

        var channels = Panel();
        channels.Children.Add(Row("应用内提醒", "页面内提醒条，始终可用。", inApp));
        channels.Children.Add(Row(
            "系统通知",
            "Toast / 托盘气泡；应用不在前台时也能收到，发送失败自动降级为应用内提醒。",
            system));

        channels.Children.Add(Hint(
            "日程、倒计时、计时器与番茄钟到点都走这两个通道；提示音在下方单独设置。"));

        panel.Children.Add(Card("通知通道", "决定提醒出现的位置，不影响提醒时间。", channels));
        panel.Children.Add(BuildNotificationSoundCard(context));

        return panel;
    }

    /// <summary>
    /// 到点提示音：两段原型音效 + 静音，切换即试听。
    /// 设置中心「提醒」与时钟模块抽屉共用同一张卡，避免两处口径不一致。
    /// </summary>
    private static Border BuildNotificationSoundCard(SettingsContext context)
    {
        var selected = NotificationSoundCatalog.Normalize(
            context.Settings.Get<string>(
                PreferenceKeys.NotificationSound,
                PreferenceDefaults.NotificationSound));

        var preview = new Button
        {
            Content = "试听",
            Style = Style("Theme.Button.Ghost"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
        };
        preview.Click += (_, _) => context.NotificationSound.Play(
            context.Settings.Get<string>(
                PreferenceKeys.NotificationSound,
                PreferenceDefaults.NotificationSound));

        var choices = Choices(
            NotificationSoundCatalog.All.Select(option => (option.Id, option.Label)).ToArray(),
            selected,
            (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key })
                {
                    context.Settings.Set(PreferenceKeys.NotificationSound, key, module: "reminders");
                    context.NotificationSound.Play(key);
                }
            },
            "SettingsNotificationSound");

        var stack = Panel();
        stack.Children.Add(Row(
            "提示音",
            "计时器与番茄钟结束时播放；切换选项即试听。",
            preview));
        stack.Children.Add(choices);
        stack.Children.Add(Hint("选择“静音”后只保留提醒条与系统通知，不播放声音。"));

        return Card("到点提示音", "两段音效来自原型，随程序发布。", stack);
    }

    private static StackPanel BuildCountdown(SettingsContext context)
    {
        var panel = Panel();
        panel.Children.Add(BuildModuleWidgetCard(
            context,
            "桌面小组件",
            "绑定单个倒计时条目；在列表放大态选择「添加到桌面」可切换绑定。",
            (WidgetKind.Countdown, "倒计时小组件", "天数、重复规则、下次日期与进度。")));

        panel.Children.Add(BuildReminderCard(
            context,
            "倒计时默认提醒",
            PreferenceKeys.CountdownReminder,
            PreferenceDefaults.CountdownReminder,
            "countdown",
            "新建倒计时时默认选中的提醒档位；周期实例按下次发生日计算。"));

        return panel;
    }

    private static StackPanel BuildTodo(SettingsContext context)
    {
        var panel = Panel();
        panel.Children.Add(BuildCompletionCard(context));
        panel.Children.Add(Hint("待办的筛选与排序在列表页直接操作；此处只保留全局完成风格。"));
        return panel;
    }

    private static StackPanel BuildClipboard(SettingsContext context)
    {
        var panel = Panel();

        var enabled = Toggle(
            "启用",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardCaptureEnabled,
                PreferenceDefaults.ClipboardCaptureEnabled),
            null);
        enabled.Click += (_, _) =>
            context.Settings.Set(
                PreferenceKeys.ClipboardCaptureEnabled,
                enabled.IsChecked == true,
                module: "clipboard");

        var paused = Toggle(
            "暂停采集",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardPaused,
                PreferenceDefaults.ClipboardPaused),
            null);
        paused.Click += (_, _) =>
            context.Settings.Set(
                PreferenceKeys.ClipboardPaused,
                paused.IsChecked == true,
                module: "clipboard");

        var capture = Panel();
        capture.Children.Add(Row(
            "启用剪贴板监听",
            "关闭后不再监听系统剪贴板，已采集历史保留。",
            enabled));
        capture.Children.Add(Row(
            "暂停采集",
            "临时停止写入；恢复后从下一次复制继续。",
            paused));
        capture.Children.Add(Hint(
            "采集在独立 STA 线程完成；图片落盘并生成缩略图，同内容 5 秒内合并为一条。"));

        panel.Children.Add(Card("采集", "控制监听与写入，开关即时生效。", capture));
        panel.Children.Add(BuildClipboardRetentionCard(context));
        panel.Children.Add(BuildClipboardPrivacyCard(context));
        panel.Children.Add(BuildClipboardPasteCard(context));
        panel.Children.Add(BuildClipboardListCard(context));

        return panel;
    }

    private static Border BuildClipboardRetentionCard(SettingsContext context)
    {
        var limitBox = new TextBox
        {
            Text = context.ClipboardRetention.Limit.ToString(CultureInfo.InvariantCulture),
            Style = Style("Theme.Input"),
            Width = 96,
            MaxLength = 4,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var apply = new Button
        {
            Content = "应用",
            Style = Style("Theme.Button.Primary"),
            Padding = new Thickness(14, 6, 14, 6),
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(limitBox);
        controls.Children.Add(apply);

        var hint = Hint(
            $"当前上限 {ClipboardRetentionService.DescribeLimit(context.ClipboardRetention.Limit)}；"
            + "0 表示不限制。");

        apply.Click += (_, _) =>
        {
            var value = int.TryParse(
                limitBox.Text?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0;

            var cleaned = context.ClipboardRetention.SetLimit(value);
            var applied = context.ClipboardRetention.Limit;
            limitBox.Text = applied.ToString(CultureInfo.InvariantCulture);
            hint.Text = cleaned > 0
                ? $"当前上限 {ClipboardRetentionService.DescribeLimit(applied)}；"
                    + $"本次清理 {cleaned} 条最旧非固定记录。"
                : $"当前上限 {ClipboardRetentionService.DescribeLimit(applied)}；本次无需清理。";
        };

        var stack = Panel();
        stack.Children.Add(Row(
            "保留上限",
            "范围 0–9999；固定条目不参与计数，也不会被淘汰。",
            controls));
        stack.Children.Add(hint);

        return Card(
            "保留容量",
            "输入非数字或空值按 0（不限制）保存；修改后立即按新上限清理。",
            stack);
    }

    private static Border BuildClipboardPrivacyCard(SettingsContext context)
    {
        var rulesBox = new TextBox
        {
            Text = string.Join(Environment.NewLine, context.ClipboardIgnoreRules.Rules),
            Style = Style("Theme.Input"),
            Height = 92,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var save = new Button
        {
            Content = "保存规则",
            Style = Style("Theme.Button.Primary"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var reset = new Button
        {
            Content = "恢复默认",
            Style = Style("Theme.Button.Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(save);
        actions.Children.Add(reset);

        var hint = Hint(
            "每行一条，按进程名或窗口标题关键字做大小写不敏感匹配；"
            + "留空保存表示不添加自定义规则。");

        save.Click += (_, _) =>
        {
            var lines = (rulesBox.Text ?? string.Empty).Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.RemoveEmptyEntries);
            var saved = context.ClipboardIgnoreRules.SaveRules(lines);
            rulesBox.Text = string.Join(Environment.NewLine, saved);
            hint.Text = saved.Count == 0
                ? "已保存 0 条自定义规则；内置密码管理器仍会拦截。"
                : $"已保存 {saved.Count} 条规则（大小写不敏感）。";
        };

        reset.Click += (_, _) =>
        {
            var defaults = context.ClipboardIgnoreRules.ResetDefaults();
            rulesBox.Text = string.Join(Environment.NewLine, defaults);
            hint.Text = $"已恢复默认规则（{defaults.Count} 条）。";
        };

        var otp = Toggle(
            "拦截 4–8 位纯数字",
            context.ClipboardIgnoreRules.BlockOneTimeCodes,
            null);
        otp.Click += (_, _) => context.ClipboardIgnoreRules.SetBlockOneTimeCodes(
            otp.IsChecked == true);

        var stack = Panel();
        stack.Children.Add(new TextBlock
        {
            Text = "忽略规则",
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        stack.Children.Add(rulesBox);
        stack.Children.Add(actions);
        stack.Children.Add(hint);
        stack.Children.Add(Row(
            "纯数字验证码",
            "关闭后允许采集纯 4–8 位数字；密码类格式与内置密码管理器始终拦截。",
            otp));

        return Card(
            "隐私排除",
            "命中规则时丢弃本次复制；内置 1Password / KeePassXC / Bitwarden / LastPass / Dashlane 进程，"
            + "以及拾光自身的保险库窗口（密码生成器 / 条目编辑）始终不采集。",
            stack);
    }

    private static Border BuildClipboardListCard(SettingsContext context)
    {
        var tags = Toggle(
            "显示标签",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardShowTags,
                PreferenceDefaults.ClipboardShowTags),
            null);
        tags.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.ClipboardShowTags,
            tags.IsChecked == true,
            module: "clipboard");

        var mergeSources = Toggle(
            "相同文本合并多来源",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardMergeSources,
                PreferenceDefaults.ClipboardMergeSources),
            null);
        mergeSources.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.ClipboardMergeSources,
            mergeSources.IsChecked == true,
            module: "clipboard");

        var stack = Panel();
        stack.Children.Add(Row(
            "列表标签",
            "关闭后列表不再显示标签 chip，同时收起标签筛选栏；已保存的标签不会删除。",
            tags));
        stack.Children.Add(Row(
            "多来源合并",
            "开启后，纯文本相同但来源不同的记录合并为一条，并保留全部来源；"
            + "关闭后按来源分别记录。",
            mergeSources));
        stack.Children.Add(Hint(
            "标签最多 5 个、每个 12 字；在条目展开详情中点击「备注与标签」编辑。"));

        return Card("列表与标签", "控制列表页的标签展示与筛选入口。", stack);
    }

    private static Border BuildClipboardPasteCard(SettingsContext context)
    {
        var minimize = Toggle(
            "双击回填后最小化主窗口",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardMinimizeAfterPaste,
                PreferenceDefaults.ClipboardMinimizeAfterPaste),
            null);
        minimize.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.ClipboardMinimizeAfterPaste,
            minimize.IsChecked == true,
            module: "clipboard");

        var htmlPlain = Toggle(
            "HTML 条目仅回填纯文本",
            context.Settings.Get<bool>(
                PreferenceKeys.ClipboardHtmlPlainOnly,
                PreferenceDefaults.ClipboardHtmlPlainOnly),
            null);
        htmlPlain.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.ClipboardHtmlPlainOnly,
            htmlPlain.IsChecked == true,
            module: "clipboard");

        var hints = Panel();
        hints.Children.Add(Row(
            "回填后最小化",
            "开启后，双击历史条目回填并返回原应用粘贴；关闭则停留在拾光。",
            minimize));
        hints.Children.Add(Row(
            "HTML 仅回填纯文本",
            "开启后 HTML 条目只写纯文本、不写富文本；关闭时按 CF_HTML + 纯文本回退写入。",
            htmlPlain));
        hints.Children.Add(Hint(
            "剪贴板浮窗：按 Ctrl + Alt + V 打开；分类、搜索、详情、滑动编辑/删除与多选能力与主页一致。"
            + "双击条目回填后浮窗自动隐藏，右上角「固定」可保持常驻。"));

        return Card("回填与浮窗", "控制双击回填后的窗口行为。", hints);
    }

    private static StackPanel BuildAbout(SettingsContext context)
    {
        var panel = Panel();

        var versionStack = Panel();
        versionStack.Children.Add(InfoRow("应用版本", $"{AppInfo.Name} {AppInfo.Version}"));
        versionStack.Children.Add(InfoRow("产品名称", AppInfo.EnglishName));
        versionStack.Children.Add(InfoRow("数据模式", "SQLite · WAL（迁移由应用自动管理）"));
        panel.Children.Add(Card("应用信息", "版本与本地数据基线。", versionStack));

        panel.Children.Add(BuildCompatibilityCard(context));

        var paths = Panel();
        paths.Children.Add(InfoRow("数据目录", AppPaths.Root, monospace: true));
        paths.Children.Add(InfoRow("数据库", AppPaths.DatabaseFile, monospace: true));
        paths.Children.Add(InfoRow("日志目录", AppPaths.LogsDirectory, monospace: true));
        paths.Children.Add(Hint("数据目录可整体复制；卸载或重装不会删除该目录，建议定期导出备份。"));
        panel.Children.Add(Card("数据与日志", "用于排查与手动备份。", paths));

        panel.Children.Add(BuildBackupCard(context));

        var hotkeyStack = Panel();
        hotkeyStack.Children.Add(InfoRow("全局热键", context.HotkeyStatus));
        hotkeyStack.Children.Add(Hint("完整 9 项快捷键在“快捷键”分组录制、清空与恢复。"));
        panel.Children.Add(Card("运行状态", "启动时注册结果。", hotkeyStack));

        var runtimeStack = Panel();
        runtimeStack.Children.Add(InfoRow("关闭窗口", "隐藏到托盘，进程继续运行"));
        runtimeStack.Children.Add(InfoRow("单实例", "再次启动会唤起已运行的窗口"));
        runtimeStack.Children.Add(InfoRow("界面语言", "简体中文"));
        panel.Children.Add(Card("运行方式", "常驻与系统集成。", runtimeStack));

        panel.Children.Add(BuildOnboardingCard(context));

        return panel;
    }

    /// <summary>关于页的首次引导入口：随时重开引导，便于用户回看隐私与主密码说明。</summary>
    private static Border BuildOnboardingCard(SettingsContext context)
    {
        var stack = Panel();
        stack.Children.Add(Hint(
            "首次引导包含主题、开机自启、剪贴板隐私说明与主密码入口；随时可以重新打开。"));
        var replay = new Button
        {
            Content = "重新打开首次引导",
            Style = Style("Theme.Button.Secondary"),
            Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = context.RequestOnboarding is not null,
        };
        replay.Click += (_, _) => context.RequestOnboarding?.Invoke();
        stack.Children.Add(replay);

        return Card("首次引导", "重新查看引导内容。", stack);
    }

    /// <summary>
    /// 兼容性诊断卡：展示启动时探测到的系统档位、DPI 感知、显示器与能力缺失，
    /// 让用户和支持人员能直接对照日志，而不是靠猜。
    /// </summary>
    private static Border BuildCompatibilityCard(SettingsContext context)
    {
        var stack = Panel();
        var snapshot = context.Compatibility;

        if (snapshot is null)
        {
            stack.Children.Add(InfoRow("运行环境", "探测中，首帧完成后自动刷新。"));
            return Card("兼容性诊断", "系统能力探测结果与自动降级项。", stack);
        }

        stack.Children.Add(InfoRow("系统", $"{snapshot.TierLabel}（build {snapshot.BuildNumber}）"));
        stack.Children.Add(InfoRow("DPI 感知", snapshot.DpiAwarenessLabel));
        stack.Children.Add(InfoRow("显示器", snapshot.DisplaySummary));
        stack.Children.Add(InfoRow("OCR", snapshot.OcrSummary));
        stack.Children.Add(InfoRow("音频输出", snapshot.AudioSummary));
        stack.Children.Add(InfoRow("窗口捕获", snapshot.WgcSummary));

        if (snapshot.Degradations.Count == 0)
        {
            stack.Children.Add(Hint("未发现降级项：当前环境按完整能力运行。"));
        }
        else
        {
            foreach (var note in snapshot.Degradations)
            {
                stack.Children.Add(Hint(note));
            }
        }

        return Card("兼容性诊断", "系统能力探测结果与自动降级项。", stack);
    }

    /// <summary>
    /// 备份与恢复卡：导出整库快照、选择备份排队恢复、打开备份目录。
    /// 恢复不在运行期替换库文件，而是写入请求、下次启动应用，避免损坏正在使用的连接。
    /// </summary>
    private static Border BuildBackupCard(SettingsContext context)
    {
        var stack = Panel();
        var backup = context.Backup;

        if (backup is null)
        {
            stack.Children.Add(Hint("备份协调器未接入，当前仅支持手动复制数据目录。"));
            return Card("备份与恢复", "导出整库快照或从备份恢复。", stack);
        }

        stack.Children.Add(InfoRow("备份目录", backup.BackupDirectory, monospace: true));

        var latest = FindLatestBackup(backup.BackupDirectory);
        stack.Children.Add(InfoRow(
            "最近备份",
            latest is null
                ? "暂无手动导出或自动快照"
                : $"{latest.Name}（{latest.LastWriteTime:yyyy-MM-dd HH:mm}，{FormatSize(latest.Length)}）"));

        var pendingLabel = new TextBlock
        {
            Style = Style("Theme.Text.Secondary"),
            TextWrapping = TextWrapping.Wrap,
        };

        var pendingRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pendingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        pendingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pendingRow.Children.Add(new TextBlock
        {
            Text = "待恢复",
            Style = Style("Theme.Text.Tertiary"),
            VerticalAlignment = VerticalAlignment.Top,
        });
        Grid.SetColumn(pendingLabel, 1);
        pendingRow.Children.Add(pendingLabel);

        void RefreshPending()
        {
            var pending = backup.PendingRestore;
            pendingLabel.Text = pending is null
                ? "无"
                : $"{Path.GetFileName(pending.BackupPath)}（{pending.RequestedAt:yyyy-MM-dd HH:mm}，重启后生效）";
            pendingLabel.Foreground = pending is null
                ? Brush("Theme.TextSecondaryBrush")
                : Brush("Theme.AccentBrush");
        }

        RefreshPending();
        stack.Children.Add(pendingRow);

        var hint = Hint("备份为整库快照：包含加密保险库密文，不包含明文密码；恢复前会自动生成覆盖前快照。");

        var export = new Button
        {
            Content = "导出备份…",
            Style = Style("Theme.Button.Primary"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var restore = new Button
        {
            Content = "从备份恢复…",
            Style = Style("Theme.Button.Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var openFolder = new Button
        {
            Content = "打开备份目录",
            Style = Style("Theme.Button.Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var cancel = new Button
        {
            Content = "取消待恢复",
            Style = Style("Theme.Button.Ghost"),
            Padding = new Thickness(12, 6, 12, 6),
            Visibility = backup.PendingRestore is null ? Visibility.Collapsed : Visibility.Visible,
        };

        export.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出拾光备份",
                Filter = "拾光备份 (*.db)|*.db|所有文件 (*.*)|*.*",
                DefaultExt = ".db",
                FileName = $"lumina-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
                InitialDirectory = backup.BackupDirectory,
                AddExtension = true,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var result = await backup.ExportAsync(dialog.FileName);
                hint.Text = $"已导出：{result.Path}（{FormatSize(result.SizeBytes)}，schema v{result.SchemaVersion}）";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                hint.Text = $"导出失败：{ex.Message}";
            }
        };

        restore.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择拾光备份",
                Filter = "拾光备份 (*.db)|*.db|所有文件 (*.*)|*.*",
                InitialDirectory = backup.BackupDirectory,
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            hint.Text = "正在校验备份…";
            var validation = await backup.ValidateAsync(dialog.FileName);
            if (!validation.IsValid)
            {
                hint.Text = $"备份不可用：{validation.Error}";
                return;
            }

            var confirm = MessageBox.Show(
                $"将使用以下备份覆盖当前数据：\n{dialog.FileName}\n\n"
                    + $"备份 schema：v{validation.SchemaVersion}\n"
                    + "恢复将在下次启动时生效，覆盖前会自动生成快照。\n\n是否继续？",
                "从备份恢复",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
            {
                hint.Text = "已取消恢复。";
                return;
            }

            try
            {
                await backup.QueueRestoreAsync(dialog.FileName);
                cancel.Visibility = Visibility.Visible;
                RefreshPending();
                hint.Text = "已安排恢复：请重启拾光（托盘 → 退出程序 后重新打开）以应用备份。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hint.Text = $"安排恢复失败：{ex.Message}";
            }
        };

        openFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(backup.BackupDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = backup.BackupDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            {
                hint.Text = $"无法打开备份目录：{ex.Message}";
            }
        };

        cancel.Click += (_, _) =>
        {
            backup.CancelPendingRestore();
            cancel.Visibility = Visibility.Collapsed;
            RefreshPending();
            hint.Text = "已取消待恢复请求，当前数据保持不变。";
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(export);
        actions.Children.Add(restore);
        actions.Children.Add(openFolder);
        actions.Children.Add(cancel);

        stack.Children.Add(actions);
        stack.Children.Add(hint);

        return Card(
            "备份与恢复",
            "恢复只写请求、下次启动应用；适用于换机、重装或误删后的数据找回。",
            stack);
    }

    private static FileInfo? FindLatestBackup(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            return new DirectoryInfo(directory)
                .GetFiles("*.db", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024.0 / 1024.0:F1} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    private static StackPanel BuildModuleBoundary(
        SettingsContext context,
        string moduleId,
        string moduleTitle)
    {
        var panel = Panel();
        var notice = new TextBlock
        {
            Text = $"“{moduleTitle}”暂无专属设置；可在设置中心查看全局偏好与快捷键。",
            Style = Style("Theme.Text.Secondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0),
        };

        panel.Children.Add(Card("模块设置", "该模块暂无专属设置。", notice));
        return panel;
    }

    private static StackPanel BuildCaptureInfo(SettingsContext context, bool isModule = false)
    {
        var panel = Panel();

        // 总设置保持“能力说明”定位，具体可编辑项统一收在截图模块抽屉里。
        if (!isModule)
        {
            var limit = context.Screenshots.RetentionLimit();
            var thumbOnlyEnabled = context.Settings.Get<bool>(
                PreferenceKeys.ScreenshotThumbOnly,
                PreferenceDefaults.ScreenshotThumbOnly);

            var info = Panel();
            info.Children.Add(InfoRow("截图保留", $"最近 {limit} 张，超出后自动淘汰"));
            info.Children.Add(InfoRow(
                "缩略图",
                thumbOnlyEnabled ? "仅保留缩略图（新截图不保存原图）" : "保留原图；可在截图页切换为仅缩略图"));
            info.Children.Add(Hint("保留张数、缩略图策略与截图快捷键在「截图」模块设置中调整。"));
            info.Children.Add(Hint("区域标注、放大镜取色与 OCR 均在截图页直接使用，无需额外配置。"));
            panel.Children.Add(Card("截图与取色", "能力说明与配置入口。", info));
            return panel;
        }

        var limitBox = new TextBox
        {
            Text = context.Screenshots.RetentionLimit().ToString(CultureInfo.InvariantCulture),
            Style = Style("Theme.Input"),
            Width = 96,
            MaxLength = 3,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var apply = new Button
        {
            Content = "应用",
            Style = Style("Theme.Button.Primary"),
            Padding = new Thickness(14, 6, 14, 6),
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(limitBox);
        controls.Children.Add(apply);

        var hint = Hint(
            $"当前保留最近 {context.Screenshots.RetentionLimit()} 张；范围 "
            + $"{AppLimits.ScreenshotRetentionMin}–{AppLimits.ScreenshotRetentionMax} 张，"
            + "超出后按最旧优先淘汰。");

        apply.Click += (_, _) =>
        {
            if (!int.TryParse(
                    limitBox.Text?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                || parsed is < AppLimits.ScreenshotRetentionMin or > AppLimits.ScreenshotRetentionMax)
            {
                hint.Text =
                    $"请输入 {AppLimits.ScreenshotRetentionMin}–{AppLimits.ScreenshotRetentionMax} 之间的整数。";
                hint.Foreground = Brush("Theme.DangerBrush");
                return;
            }

            context.Settings.Set(PreferenceKeys.ScreenshotRetentionLimit, parsed, module: "screenshots");
            var cleaned = context.Screenshots.EnforceLimit(parsed);
            hint.Foreground = Brush("Theme.TextTertiaryBrush");
            hint.Text = cleaned > 0
                ? $"当前保留最近 {parsed} 张；本次清理 {cleaned} 张最旧记录。"
                : $"当前保留最近 {parsed} 张；本次无需清理。";
        };

        var retention = Panel();
        retention.Children.Add(Row(
            "截图保留",
            "超出上限立即淘汰最旧记录；磁盘空间不足 50 MB 时会拒绝保存。",
            controls));
        retention.Children.Add(hint);

        var thumbOnly = Toggle(
            "仅保留缩略图",
            context.Settings.Get<bool>(
                PreferenceKeys.ScreenshotThumbOnly,
                PreferenceDefaults.ScreenshotThumbOnly),
            null);
        thumbOnly.Click += (_, _) => context.Settings.Set(
            PreferenceKeys.ScreenshotThumbOnly,
            thumbOnly.IsChecked == true,
            module: "screenshots");

        var stack = Panel();
        stack.Children.Add(retention);
        stack.Children.Add(Row(
            "缩略图",
            "开启后新截图只保存缩略图；已有原图不会被删除。截图页右上角也可切换。",
            thumbOnly));

        stack.Children.Add(BuildCaptureHotkeyList(context));
        stack.Children.Add(Hint("区域标注、放大镜取色与 OCR 均在截图页直接使用，无需额外配置。"));
        panel.Children.Add(Card("截图与取色", "能力说明与配置入口。", stack));
        return panel;
    }

    /// <summary>
    /// 截图模块的快捷键只读清单：总设置里的「快捷键」页负责录制与恢复，
    /// 模块抽屉只展示当前生效的组合，省去在两个页面之间来回跳转。
    /// </summary>
    private static StackPanel BuildCaptureHotkeyList(SettingsContext context)
    {
        var list = Panel();
        list.Children.Add(new TextBlock
        {
            Text = "快捷键",
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 6),
        });

        string[] actions =
        [
            HotkeyActions.RegionCapture,
            HotkeyActions.FullScreenCapture,
            HotkeyActions.DelayedCapture,
            HotkeyActions.ColorPick,
            HotkeyActions.CopyColorValue,
        ];

        foreach (var action in actions)
        {
            var gesture = context.HotkeyBindings.Get(action);
            var scope = HotkeyCatalog.Defaults[action].Scope == HotkeyScope.Global ? "全局" : "页内";
            list.Children.Add(InfoRow(
                HotkeyActionCatalog.Title(action),
                HotkeyCatalog.IsUnbound(gesture)
                    ? "未设置（在「快捷键」页录制）"
                    : $"{gesture} · {scope}"));
        }

        list.Children.Add(Hint("修改或清空请到「设置 → 快捷键」；页内快捷键只在截图页聚焦时生效。"));
        return list;
    }

    private static StackPanel BuildFocusInfo(SettingsContext context)
    {
        var panel = Panel();
        var stack = Panel();
        stack.Children.Add(InfoRow("每日目标", "在专注统计页顶部设置"));
        stack.Children.Add(InfoRow("会话来源", "番茄钟与计时器"));
        stack.Children.Add(Hint("会话记录、热力图、周期统计与导出均在专注统计页。"));
        panel.Children.Add(Card("专注统计", "记录口径与配置入口。", stack));
        return panel;
    }

    private static StackPanel BuildNoteInfo(SettingsContext context)
    {
        var panel = Panel();
        var stack = Panel();
        stack.Children.Add(InfoRow("自动保存", "输入即保存"));
        stack.Children.Add(InfoRow("标签限制", "最多 5 个，每个 12 字"));
        stack.Children.Add(Hint("放大阅读、桌面便签组件与转待办在便签页使用。"));
        panel.Children.Add(Card("便签", "保存与标签规则。", stack));
        return panel;
    }

    private static StackPanel BuildVaultInfo(SettingsContext context)
    {
        var panel = Panel();
        var stack = Panel();
        stack.Children.Add(InfoRow("加密方式", "PBKDF2-SHA256 + AES-GCM"));
        stack.Children.Add(InfoRow("自动锁定", "在密码管理器左栏设置"));
        stack.Children.Add(Hint("锁屏与睡眠会立即锁定；复制后清除时长同在密码管理器左栏设置。"));
        panel.Children.Add(Card("密码管理器", "安全边界与配置入口。", stack));
        return panel;
    }

    private static Border BuildAutoStartCard(SettingsContext context)
    {
        var toggle = Toggle("随 Windows 启动", false, null);
        var hint = Hint(string.Empty);
        hint.Margin = new Thickness(0, 4, 0, 0);

        var update = new Action(() =>
        {
            try
            {
                toggle.IsChecked = context.AutoStartup.IsEnabled();
                hint.Text = string.Empty;
            }
            catch (Exception ex)
            {
                toggle.IsChecked = false;
                hint.Text = $"无法读取系统自启状态：{ex.Message}";
            }
        });

        toggle.Click += (_, _) =>
        {
            var desired = toggle.IsChecked == true;
            try
            {
                context.AutoStartup.SetEnabled(desired);
                hint.Text = desired
                    ? "已写入当前用户启动项。"
                    : "已移除当前用户启动项。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"开机自启设置失败：{ex.Message}",
                    "拾光",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                update();
            }
        };

        update();

        var stack = Panel();
        stack.Children.Add(Row(
            "开机自启",
            "写入当前用户 Run 键；状态以系统实际值为准。",
            toggle));
        stack.Children.Add(hint);

        return Card("启动", "关闭窗口默认隐藏到托盘，计时与音频不中断。", stack);
    }

    /// <summary>
    /// 模块设置里的小组件卡片：每个组件独立控制显示、置顶、穿透、不透明度与皮肤。
    /// 除显示开关外都通过 <see cref="IWidgetHost.UpdateWidget"/> 即时应用，不强制显示组件。
    /// </summary>
    private static Border BuildModuleWidgetCard(
        SettingsContext context,
        string title,
        string subtitle,
        params (WidgetKind Kind, string Name, string Description)[] widgets)
    {
        var stack = Panel();
        for (var index = 0; index < widgets.Length; index++)
        {
            if (index > 0)
            {
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush("Theme.LineBrush"),
                    Margin = new Thickness(0, 12, 0, 14),
                });
            }

            stack.Children.Add(BuildModuleWidgetBlock(
                context,
                widgets[index].Kind,
                widgets[index].Name,
                widgets[index].Description));
        }

        stack.Children.Add(Hint(
            "显示、置顶、穿透、不透明度与皮肤都按组件分别保存；可拖动卡片移动，靠近屏幕边缘自动吸附。"));

        return Card(title, subtitle, stack);
    }

    private static StackPanel BuildModuleWidgetBlock(
        SettingsContext context,
        WidgetKind kind,
        string name,
        string description)
    {
        var state = context.Widgets.GetState(kind);
        var panel = Panel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = Panel();
        text.Children.Add(new TextBlock
        {
            Text = name,
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Style = Style("Theme.Text.Tertiary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        header.Children.Add(text);

        var visibility = Toggle("显示", context.Widgets.IsVisible(kind), null);
        AutomationProperties.SetName(visibility, $"{name} 显示");
        visibility.VerticalAlignment = VerticalAlignment.Top;
        visibility.Click += (_, _) =>
        {
            if (visibility.IsChecked == true)
            {
                context.Widgets.ShowWidget(context.Widgets.GetState(kind));
            }
            else
            {
                context.Widgets.CloseWidget(kind);
            }
        };
        Grid.SetColumn(visibility, 1);
        header.Children.Add(visibility);
        panel.Children.Add(header);

        var topmost = Toggle("置顶", state.Topmost, null);
        AutomationProperties.SetName(topmost, $"{name} 置顶");
        topmost.Click += (_, _) =>
        {
            context.Widgets.UpdateWidget(context.Widgets.GetState(kind) with
            {
                Topmost = topmost.IsChecked == true,
            });
        };
        panel.Children.Add(Row(
            "置顶显示",
            "关闭后固定在桌面层（可被其它窗口遮挡），在桌面上仍可拖动。",
            topmost));

        var clickThrough = Toggle("整窗穿透", state.ClickThrough, null);
        AutomationProperties.SetName(clickThrough, $"{name} 点击穿透");
        clickThrough.Click += (_, _) =>
        {
            context.Widgets.UpdateWidget(context.Widgets.GetState(kind) with
            {
                ClickThrough = clickThrough.IsChecked == true,
            });
        };
        panel.Children.Add(Row(
            "点击穿透",
            "开启后点击直接落到下层窗口，组件只显示不响应；拖动与按钮一并失效，在此关掉即可恢复。",
            clickThrough));

        var opacityValues = new[] { 1.0, 0.85, 0.7 };
        var opacityPanel = new WrapPanel();
        for (var index = 0; index < opacityValues.Length; index++)
        {
            var opacity = opacityValues[index];
            var choice = new RadioButton
            {
                Content = $"{opacity * 100:0}%",
                Tag = opacity,
                GroupName = $"SettingsWidgetOpacity{kind}",
                Style = Style("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, index == opacityValues.Length - 1 ? 0 : 8, 0),
                IsChecked = Math.Abs(state.Opacity - opacity) < 0.001,
            };
            choice.Checked += (sender, _) =>
            {
                if (sender is not RadioButton { Tag: double selected })
                {
                    return;
                }

                context.Widgets.UpdateWidget(context.Widgets.GetState(kind) with
                {
                    Opacity = selected,
                });
            };
            opacityPanel.Children.Add(choice);
        }

        AutomationProperties.SetName(opacityPanel, $"{name} 不透明度");
        panel.Children.Add(Row(
            "不透明度",
            "整窗透明度，与玻璃材质叠加；档位按组件分别保存。",
            opacityPanel));

        var skins = WidgetSkinCatalog.All;
        var skin = new ComboBox
        {
            Style = Style("Theme.ComboBox"),
            Width = 132,
            ItemsSource = skins.Select(option => option.Label).ToArray(),
            SelectedIndex = Math.Max(0, skins.ToList().FindIndex(option => option.Value == state.Skin)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(skin, $"{name} 皮肤");
        skin.SelectionChanged += (_, _) =>
        {
            if (skin.SelectedIndex < 0
                || skin.SelectedIndex >= skins.Count
                || skins[skin.SelectedIndex].Value == context.Widgets.GetState(kind).Skin)
            {
                return;
            }

            context.Widgets.UpdateWidget(context.Widgets.GetState(kind) with
            {
                Skin = skins[skin.SelectedIndex].Value,
            });
        };

        var skinRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        skinRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        skinRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        skinRow.Children.Add(new TextBlock
        {
            Text = "皮肤",
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(skin, 1);
        skinRow.Children.Add(skin);
        panel.Children.Add(skinRow);
        panel.Children.Add(Hint(
            "亚克力与液态玻璃会抓取组件背后的桌面：静止时每 0.2 秒刷新，"
            + "拖动窗口、移动鼠标或播放视频时自动提到约 30 帧；抓屏瞬间组件自身不进入画面。"
            + "透明为直接透出、不模糊。"));

        return panel;
    }

    private static Border BuildReminderCard(
        SettingsContext context,
        string title,
        string preferenceKey,
        string fallback,
        string module,
        string subtitle)
    {
        var selected = context.Settings.Get<string>(preferenceKey, fallback) ?? fallback;
        var suppress = true;

        var choices = Choices(
            ReminderOptions,
            selected,
            (sender, _) =>
            {
                if (suppress || sender is not RadioButton { Tag: string key })
                {
                    return;
                }

                context.Settings.Set(preferenceKey, key, module: module);
            },
            "Settings" + preferenceKey);

        suppress = false;
        return Card(title, subtitle, choices);
    }

    private static Border Card(string title, string? subtitle, UIElement content)
    {
        var stack = Panel();

        var titleText = new TextBlock
        {
            Text = title,
            Style = Style("Theme.Text.SectionTitle"),
        };
        titleText.Margin = subtitle is null
            ? new Thickness(0, 0, 0, 10)
            : new Thickness(0, 0, 0, 2);
        stack.Children.Add(titleText);

        if (subtitle is not null)
        {
            var subtitleText = new TextBlock
            {
                Text = subtitle,
                Style = Style("Theme.Text.Tertiary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            stack.Children.Add(subtitleText);
        }

        stack.Children.Add(content);

        return new Border
        {
            Style = Style("Theme.Card"),
            Margin = new Thickness(0, 0, 0, 16),
            Child = stack,
        };
    }

    private static Grid Row(string title, string? description, FrameworkElement? control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = Panel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = Style("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
        });

        if (description is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                Style = Style("Theme.Text.Tertiary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        grid.Children.Add(text);

        if (control is not null)
        {
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return grid;
    }

    private static WrapPanel Choices(
        (string Key, string Label)[] options,
        string? selected,
        RoutedEventHandler handler,
        string groupName)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };

        foreach (var (key, label) in options)
        {
            var choice = new RadioButton
            {
                Content = label,
                Tag = key,
                GroupName = groupName,
                Style = Style("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = selected is not null && key == selected,
            };
            choice.Checked += handler;
            panel.Children.Add(choice);
        }

        return panel;
    }

    private static CheckBox Toggle(string content, bool isChecked, RoutedEventHandler? handler)
    {
        var toggle = new CheckBox
        {
            Content = content,
            Style = Style("Theme.Toggle"),
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (handler is not null)
        {
            toggle.Click += handler;
        }

        return toggle;
    }

    private static Grid InfoRow(string label, string value, bool monospace = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = Style("Theme.Text.Tertiary"),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var valueText = new TextBlock
        {
            Text = value,
            Style = Style("Theme.Text.Secondary"),
            TextWrapping = TextWrapping.Wrap,
        };

        if (monospace)
        {
            valueText.FontFamily = (FontFamily)Application.Current.FindResource("Theme.FontFamilyMono");
            valueText.FontSize = 12;
        }

        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Style = Style("Theme.Text.Tertiary"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static StackPanel Panel() => new();

    private static Style Style(string key) => (Style)Application.Current.FindResource(key);

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
