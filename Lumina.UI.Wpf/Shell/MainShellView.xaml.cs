using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Backup;
using Lumina.Services.Capture;
using Lumina.Services.Clipboard;
using Lumina.Services.Colors;
using Lumina.Services.Compatibility;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Notifications;
using Lumina.Services.Reminders;
using Lumina.UI.Wpf.Calendar;
using Lumina.UI.Wpf.Capture;
using Lumina.UI.Wpf.Clipboard;
using Lumina.UI.Wpf.Clock;
using Lumina.UI.Wpf.Controls;
using Lumina.UI.Wpf.Countdown;
using Lumina.UI.Wpf.Focus;
using Lumina.UI.Wpf.Notes;
using Lumina.UI.Wpf.Settings;
using Lumina.UI.Wpf.Themes;
using Lumina.UI.Wpf.Todos;
using Lumina.UI.Wpf.Vault;

namespace Lumina.UI.Wpf.Shell;

/// <summary>
/// 主窗口内容：左侧九模块导航、模块页与 P1-11 设置中枢。
/// 设置中心与页内齿轮抽屉共享同一 <see cref="ISettingsStore"/>。
/// </summary>
public partial class MainShellView : UserControl
{
    private readonly IClockService _clockService;
    private readonly SettingsContext _settingsContext;
    private SettingsCenterView? _settingsCenter;
    private readonly ModuleDrawerHost _drawer;
    private CaptureView? _captureView;
    private NoteView? _noteView;
    private readonly Dictionary<string, Func<FrameworkElement>> _viewFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _views = new(StringComparer.Ordinal);
    private readonly ReminderScheduler _reminderScheduler;
    private readonly SessionCompletionNotifier _completionNotifier;
    private readonly Queue<ShellNotice> _noticeQueue = new();
    private readonly DispatcherTimer _reminderTimer;

    private ShellNotice? _currentNotice;
    private bool _clockSubscribed;
    private bool _reminderSubscribed;
    private bool _completionSubscribed;

    /// <summary>提醒条的统一数据：日程提醒与计时结束共用同一队列与样式。</summary>
    private sealed record ShellNotice(string Title, string Body, string? Route);

    /// <summary>计时器请求缩小为小组件：由主窗口把自身最小化。</summary>
    public event EventHandler? WidgetMiniRequested;

    /// <summary>请求重新打开首次运行引导。</summary>
    public event EventHandler? OnboardingRequested;

    public MainShellView(
        ThemeManager themeManager,
        IAutoStartupService autoStartupService,
        IWidgetHost widgetHost,
        IClockService clockService,
        IWorldClockService worldClockService,
        ICalendarService calendarService,
        IScheduleService scheduleService,
        ITodoService todoService,
        CompletionStyleHub completionStyles,
        ITimerEngine timerEngine,
        IStopwatchEngine stopwatchEngine,
        IPomodoroService pomodoroService,
        INoiseMixer noiseMixer,
        ISettingsStore settings,
        IHotkeyManager hotkeyManager,
        HotkeyBindingService hotkeyBindings,
        ClipboardRetentionService clipboardRetention,
        IgnoreRuleService clipboardIgnoreRules,
        ClipboardBoardService clipboardBoard,
        ClipboardFloatController clipboardFloat,
        IClipboardMonitor clipboardMonitor,
        CaptureFlowController captureFlow,
        IColorProbe colorProbe,
        ColorHistoryService colorHistory,
        IClipboardWriter clipboardWriter,
        IOcrService ocr,
        IScreenCaptureService captureService,
        IScreenshotStore screenshotStore,
        IFocusStatsService focusStats,
        IDailyGoalService dailyGoals,
        ICountdownService countdownService,
        INoteService noteService,
        IVaultService vaultService,
        ISecureClipboard secureClipboard,
        ReminderScheduler reminderScheduler,
        SessionCompletionNotifier completionNotifier,
        INotificationSoundPlayer notificationSound,
        string hotkeyStatus,
        IBackupCoordinator? backupCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(themeManager);
        ArgumentNullException.ThrowIfNull(autoStartupService);
        ArgumentNullException.ThrowIfNull(widgetHost);
        ArgumentNullException.ThrowIfNull(clockService);
        ArgumentNullException.ThrowIfNull(worldClockService);
        ArgumentNullException.ThrowIfNull(calendarService);
        ArgumentNullException.ThrowIfNull(scheduleService);
        ArgumentNullException.ThrowIfNull(todoService);
        ArgumentNullException.ThrowIfNull(completionStyles);
        ArgumentNullException.ThrowIfNull(timerEngine);
        ArgumentNullException.ThrowIfNull(stopwatchEngine);
        ArgumentNullException.ThrowIfNull(pomodoroService);
        ArgumentNullException.ThrowIfNull(noiseMixer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeyManager);
        ArgumentNullException.ThrowIfNull(hotkeyBindings);
        ArgumentNullException.ThrowIfNull(clipboardRetention);
        ArgumentNullException.ThrowIfNull(clipboardIgnoreRules);
        ArgumentNullException.ThrowIfNull(clipboardBoard);
        ArgumentNullException.ThrowIfNull(clipboardFloat);
        ArgumentNullException.ThrowIfNull(clipboardMonitor);
        ArgumentNullException.ThrowIfNull(captureFlow);
        ArgumentNullException.ThrowIfNull(colorProbe);
        ArgumentNullException.ThrowIfNull(colorHistory);
        ArgumentNullException.ThrowIfNull(clipboardWriter);
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(screenshotStore);
        ArgumentNullException.ThrowIfNull(focusStats);
        ArgumentNullException.ThrowIfNull(dailyGoals);
        ArgumentNullException.ThrowIfNull(countdownService);
        ArgumentNullException.ThrowIfNull(noteService);
        ArgumentNullException.ThrowIfNull(vaultService);
        ArgumentNullException.ThrowIfNull(secureClipboard);
        ArgumentNullException.ThrowIfNull(reminderScheduler);
        ArgumentNullException.ThrowIfNull(completionNotifier);
        ArgumentNullException.ThrowIfNull(notificationSound);
        ArgumentNullException.ThrowIfNull(hotkeyStatus);

        _clockService = clockService;
        _reminderScheduler = reminderScheduler;
        _completionNotifier = completionNotifier;
        _reminderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(12),
        };
        _reminderTimer.Tick += (_, _) => CloseReminder();

        InitializeComponent();
        WheelRouter.Initialize();
        WheelScrollChaining.Attach(this);

        _settingsContext = new SettingsContext(
            themeManager,
            settings,
            completionStyles,
            autoStartupService,
            widgetHost,
            worldClockService,
            noiseMixer,
            hotkeyManager,
            hotkeyBindings,
            clipboardRetention,
            clipboardIgnoreRules,
            screenshotStore,
            notificationSound,
            hotkeyStatus);

        _settingsContext.Backup = backupCoordinator;
        _settingsContext.RequestOnboarding = () =>
            OnboardingRequested?.Invoke(this, EventArgs.Empty);

        // P4-02：首屏只实例化时钟；其余模块在首次进入时创建，
        // 避免冷启动为 10 个视图同时付 XAML 解析、数据加载与定时器启动成本。
        _viewFactories["clock"] = () =>
        {
            var clockPanel = new StackPanel();
            clockPanel.Children.Add(new ClockView(clockService, worldClockService, settings));
            clockPanel.Children.Add(new WorldClockView(clockService, worldClockService)
            {
                Margin = new Thickness(0, 16, 0, 0),
            });

            var timerView = new TimerView(timerEngine, stopwatchEngine, pomodoroService, widgetHost, settings)
            {
                Margin = new Thickness(0, 16, 0, 0),
            };
            timerView.MiniRequested += (_, _) => WidgetMiniRequested?.Invoke(this, EventArgs.Empty);
            clockPanel.Children.Add(timerView);

            clockPanel.Children.Add(new NoiseMixerView(noiseMixer)
            {
                Margin = new Thickness(0, 16, 0, 0),
            });

            return new ScrollViewer
            {
                Content = clockPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
        };
        _viewFactories["schedule"] = () => new ScheduleView(
            calendarService,
            scheduleService,
            todoService,
            completionStyles,
            themeManager,
            settings);
        _viewFactories["todo"] = () => new TodoView(todoService, completionStyles, themeManager);
        _viewFactories["clipboard"] = () => new ClipboardView(
            clipboardBoard,
            clipboardMonitor,
            clipboardFloat,
            settings);
        _viewFactories["capture"] = () => new CaptureView(
            captureFlow,
            colorProbe,
            colorHistory,
            clipboardWriter,
            ocr,
            captureService,
            screenshotStore,
            settings,
            hotkeyBindings);
        _viewFactories["countdown"] = () => new CountdownView(countdownService, widgetHost, themeManager, settings);
        _viewFactories["focus"] = () => new FocusStatsView(focusStats, dailyGoals, countdownService);
        _viewFactories["note"] = () => new NoteView(noteService, todoService, widgetHost);
        _viewFactories["vault"] = () => new VaultView(vaultService, clipboardWriter, secureClipboard);
        _viewFactories["settings"] = () => new SettingsCenterView(_settingsContext);

        ClockHost.Content = EnsureModuleView("clock");

        _drawer = new ModuleDrawerHost(settings, themeManager);
        DrawerHost.Content = _drawer;

        NavList.ItemsSource = ShellModuleCatalog.All;
        NavList.SelectedIndex = 0;

        UpdateClockText();
        FooterText.Text = $"v{AppInfo.Version}";

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>兼容性探测完成后由 App 注入，设置「关于」页据此展示运行环境。</summary>
    public void SetCompatibility(CompatibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _settingsContext.Compatibility = snapshot;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_clockSubscribed)
        {
            _clockService.TickSecond += OnClockTick;
            _clockSubscribed = true;
        }

        if (!_reminderSubscribed)
        {
            _reminderScheduler.Triggered += OnReminderTriggered;
            _reminderSubscribed = true;
        }

        if (!_completionSubscribed)
        {
            _completionNotifier.Raised += OnSessionCompleted;
            _completionSubscribed = true;
        }

        UpdateClockText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _drawer.Close();
        _reminderTimer.Stop();
        _noticeQueue.Clear();
        _currentNotice = null;
        ReminderBar.Visibility = Visibility.Collapsed;

        if (_clockSubscribed)
        {
            _clockService.TickSecond -= OnClockTick;
            _clockSubscribed = false;
        }

        if (_reminderSubscribed)
        {
            _reminderScheduler.Triggered -= OnReminderTriggered;
            _reminderSubscribed = false;
        }

        if (_completionSubscribed)
        {
            _completionNotifier.Raised -= OnSessionCompleted;
            _completionSubscribed = false;
        }
    }

    /// <summary>全局热键新建便签：切到便签页并打开编辑窗。</summary>
    public void CreateNote()
    {
        NavigateTo("note");
        EnsureModuleView("note");
        _noteView?.CreateNote();
    }

    /// <summary>按模块 id 跳转；供提醒条与托盘通知使用。</summary>
    public void NavigateTo(string moduleId)
    {
        var module = ShellModuleCatalog.All.FirstOrDefault(
            item => string.Equals(item.Id, moduleId, StringComparison.Ordinal));
        if (module is not null)
        {
            NavList.SelectedItem = module;
        }
    }

    private void OnReminderTriggered(ReminderNotification notification)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnReminderTriggered(notification)));
            return;
        }

        if (!notification.AppBarRequested)
        {
            return;
        }

        EnqueueNotice(new ShellNotice(
            notification.Title,
            notification.Body,
            ReminderPlanner.RouteId(notification.Route)));
    }

    /// <summary>计时器 / 番茄钟到点：与日程提醒共用同一条应用内提醒条。</summary>
    private void OnSessionCompleted(SessionCompletionAlert alert)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSessionCompleted(alert)));
            return;
        }

        EnqueueNotice(new ShellNotice(alert.Title, alert.Body, alert.Route));
    }

    private void EnqueueNotice(ShellNotice notice)
    {
        _noticeQueue.Enqueue(notice);

        if (_currentNotice is null)
        {
            ShowNextNotice();
        }
    }

    private void ShowNextNotice()
    {
        if (_noticeQueue.Count == 0)
        {
            ReminderBar.Visibility = Visibility.Collapsed;
            return;
        }

        _currentNotice = _noticeQueue.Dequeue();
        ReminderTitle.Text = _currentNotice.Title;
        ReminderBody.Text = _currentNotice.Body;
        ReminderBar.Visibility = Visibility.Visible;
        _reminderTimer.Stop();
        _reminderTimer.Start();
    }

    private void CloseReminder()
    {
        _reminderTimer.Stop();
        _currentNotice = null;
        ReminderBar.Visibility = Visibility.Collapsed;
        ShowNextNotice();
    }

    private void OnReminderCloseClick(object sender, RoutedEventArgs e) => CloseReminder();

    private void OnReminderViewClick(object sender, RoutedEventArgs e)
    {
        var route = _currentNotice?.Route;
        CloseReminder();

        if (!string.IsNullOrWhiteSpace(route))
        {
            NavigateTo(route);
        }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ShellModule module)
        {
            return;
        }

        _drawer.Close();

        PageTitle.Text = module.Title;
        PageSubtitle.Text = module.Summary;

        // 首次进入时才构造该模块视图（P4-02 懒加载）。
        EnsureModuleView(module.Id);

        var showSettings = module.IsSettings;
        var showClock = string.Equals(module.Id, "clock", StringComparison.Ordinal);
        var showCalendar = string.Equals(module.Id, "schedule", StringComparison.Ordinal);
        var showTodo = string.Equals(module.Id, "todo", StringComparison.Ordinal);
        var showClipboard = string.Equals(module.Id, "clipboard", StringComparison.Ordinal);
        var showCapture = string.Equals(module.Id, "capture", StringComparison.Ordinal);
        var showCountdown = string.Equals(module.Id, "countdown", StringComparison.Ordinal);
        var showFocus = string.Equals(module.Id, "focus", StringComparison.Ordinal);
        var showNote = string.Equals(module.Id, "note", StringComparison.Ordinal);
        var showVault = string.Equals(module.Id, "vault", StringComparison.Ordinal);

        SettingsHost.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        ClockHost.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
        CalendarHost.Visibility = showCalendar ? Visibility.Visible : Visibility.Collapsed;
        TodoHost.Visibility = showTodo ? Visibility.Visible : Visibility.Collapsed;
        ClipboardHost.Visibility = showClipboard ? Visibility.Visible : Visibility.Collapsed;
        CaptureHost.Visibility = showCapture ? Visibility.Visible : Visibility.Collapsed;
        CountdownHost.Visibility = showCountdown ? Visibility.Visible : Visibility.Collapsed;
        FocusHost.Visibility = showFocus ? Visibility.Visible : Visibility.Collapsed;
        NoteHost.Visibility = showNote ? Visibility.Visible : Visibility.Collapsed;
        VaultHost.Visibility = showVault ? Visibility.Visible : Visibility.Collapsed;
        ModulePlaceholder.Visibility =
            !showSettings && !showClock && !showCalendar && !showTodo && !showClipboard && !showCapture
                && !showCountdown && !showFocus && !showNote && !showVault
                ? Visibility.Visible
                : Visibility.Collapsed;
        ModuleSettingsButton.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;

        if (showSettings)
        {
            _settingsCenter?.Refresh();
            return;
        }

        if (showClock || showCalendar || showTodo || showClipboard || showCapture || showCountdown || showFocus)
        {
            if (showCapture)
            {
                // 模块内快捷键（Alt+Shift+C / Ctrl+Shift+C）只在截图模块聚焦时生效。
                _captureView?.FocusForShortcuts();
            }

            return;
        }

        PlaceholderGlyph.Text = module.Title.Length > 0 ? module.Title[..1] : "拾";
        PlaceholderText.Text = "该模块暂时无法加载，请重启拾光后重试。";
    }

    /// <summary>按模块 id 创建并缓存视图；重复调用直接返回同一实例。</summary>
    private FrameworkElement EnsureModuleView(string moduleId)
    {
        if (_views.TryGetValue(moduleId, out var cached))
        {
            return cached;
        }

        if (!_viewFactories.TryGetValue(moduleId, out var factory))
        {
            throw new InvalidOperationException($"未注册模块视图：{moduleId}");
        }

        var view = factory();
        _views[moduleId] = view;

        switch (moduleId)
        {
            case "clock":
                ClockHost.Content = view;
                break;
            case "schedule":
                CalendarHost.Content = view;
                break;
            case "todo":
                TodoHost.Content = view;
                break;
            case "clipboard":
                ClipboardHost.Content = view;
                break;
            case "capture":
                CaptureHost.Content = view;
                _captureView = (CaptureView)view;
                break;
            case "countdown":
                CountdownHost.Content = view;
                break;
            case "focus":
                FocusHost.Content = view;
                break;
            case "note":
                NoteHost.Content = view;
                _noteView = (NoteView)view;
                break;
            case "vault":
                VaultHost.Content = view;
                break;
            case "settings":
                SettingsHost.Content = view;
                _settingsCenter = (SettingsCenterView)view;
                break;
            default:
                throw new InvalidOperationException($"未知模块：{moduleId}");
        }

        return view;
    }

    private void OnModuleSettingsClick(object sender, RoutedEventArgs e)
    {
        if (NavList.SelectedItem is not ShellModule module || module.IsSettings)
        {
            return;
        }

        if (_drawer.IsOpen && _drawer.CurrentModuleId == module.Id)
        {
            _drawer.Close();
            return;
        }

        _drawer.Open(
            module.Id,
            module.Title,
            () => SettingsSectionFactory.BuildModule(_settingsContext, module.Id, module.Title));
    }

    private void OnClockTick()
    {
        // TickSecond 来自后台计时器线程；不得在非 UI 线程访问 IsLoaded 等 WPF 属性。
        Dispatcher.BeginInvoke(new Action(UpdateClockText));
    }

    private void UpdateClockText()
    {
        SidebarClockText.Text = _clockService.Now(use12Hour: false, shichenMode: false)
            .Local
            .ToString("HH:mm:ss");
    }
}
