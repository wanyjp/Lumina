using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Lumina.App.Shell;
using Lumina.App.Startup;
using Lumina.Data.Backup;
using Lumina.Data.Files;
using Lumina.Data.Migrations;
using Lumina.Domain.Enums;
using Lumina.Services.Backup;
using Lumina.Services.Capture;
using Lumina.Services.Clipboard;
using Lumina.Services.Clock;
using Lumina.Services.Colors;
using Lumina.Services.Compatibility;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Diagnostics;
using Lumina.Services.Focus;
using Lumina.Services.Hotkeys;
using Lumina.Services.Notifications;
using Lumina.Services.Reminders;
using Lumina.Services.Runtime;
using Lumina.UI.Wpf.Capture;
using Lumina.UI.Wpf.Clipboard;
using Lumina.UI.Wpf.Onboarding;
using Lumina.UI.Wpf.Themes;
using Lumina.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lumina.App;

/// <summary>
/// 应用入口（Composition Root 的使用方）：单实例、日志、全局异常兜底、主题、
/// 主窗口壳、托盘、全局热键与单实例激活。
/// </summary>
public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private MainWindow? _mainWindow;
    private TrayService? _tray;
    private SingleInstanceActivation? _activation;
    private IClipboardMonitor? _clipboardMonitor;
    private ClipboardFloatController? _clipboardFloat;
    private CaptureFlowController? _captureFlow;
    private IPomodoroService? _pomodoro;
    private IVaultService? _vault;
    private HotkeyRegistrationService? _hotkeyRegistration;
    private StartupWatchdog? _startupWatchdog;
    private SystemResumeCoordinator? _resumeCoordinator;
    private System.Threading.Timer? _resumeHeartbeat;
    private CompatibilitySnapshot? _compatibility;
    private bool _compatibilityCaptured;
    private int _compatibilityAttempts;
    private Exception? _databaseFailure;
    private List<string> _hotkeyFailures = [];
    private bool _exitRequested;
    private bool _mainWindowShown;
    private FirstRunWindow? _onboarding;
    private string? _dataRootNotice;
    private bool _isExistingInstall;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 最早订阅：建目录/建容器阶段的异常也要落日志并给出提示，避免静默退出。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.TryAcquire())
        {
            Exception? signalError = null;
            if (!SingleInstanceActivation.TrySignal(out signalError))
            {
                var accessDenied = _singleInstance.LastError is UnauthorizedAccessException
                    || signalError is UnauthorizedAccessException;
                var message = accessDenied
                    ? "检测到另一个「拾光」实例正在运行，可能是以管理员权限启动的。\n\n"
                        + "普通权限无法唤起该实例。请先退出它，或使用相同的管理员权限启动拾光。"
                    : _singleInstance.LastError is not null
                        ? $"单实例检查失败：{_singleInstance.LastError.Message}\n\n"
                            + "请检查是否有其他拾光实例正在运行，然后重试。"
                        : $"{AppPaths.ProductName}已在运行，请从托盘打开主界面。";

                System.Windows.MessageBox.Show(
                    message,
                    AppPaths.ProductName,
                    MessageBoxButton.OK,
                    accessDenied || _singleInstance.LastError is not null
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Information);
            }

            Shutdown(0);
            return;
        }

        // P4-06：数据目录可写性在进入容器前检测；不可写时回退到本地应用数据/临时目录并提示。
        try
        {
            _dataRootNotice = DataRootResolver.Prepare().Notice;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"无法创建数据目录，拾光无法启动。\n\n{ex.Message}",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _services = CompositionRoot.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();

        AppLog.Startup(_logger, AppPaths.ProductName, AppInfo.Version, AppPaths.Root);
        if (_dataRootNotice is not null)
        {
            AppLog.DataRootFallback(_logger, _dataRootNotice);
        }

        // 升级用户的数据库已存在：不重复弹出首次引导，只在全新安装时显示。
        _isExistingInstall = DatabaseExists();

        // P4-04：待恢复请求必须在任何业务打开数据库之前应用（替换的是整库文件）。
        ApplyPendingRestore();

        if (!InitializeDatabase())
        {
            Shutdown(1);
            return;
        }

        ReconcileFiles();

        // P3-01：订阅计时器/番茄的会话结束事件并落库（单例由容器持有，退出时随之释放）。
        _ = _services.GetRequiredService<FocusSessionRecorder>();

        var themeManager = _services.GetRequiredService<ThemeManager>();
        themeManager.Initialize();
        AppLog.ThemeReady(
            _logger,
            themeManager.RequestedTheme,
            themeManager.EffectiveTheme,
            themeManager.IsHighContrast,
            themeManager.IsReducedMotion);

        var autoStartupService = _services.GetRequiredService<IAutoStartupService>();
        var widgetHost = _services.GetRequiredService<IWidgetHost>();
        if (widgetHost is WidgetHost widgetHostEvents)
        {
            widgetHostEvents.ReturnToMainRequested += () =>
                Dispatcher.BeginInvoke(new Action(ShowMainWindow));
        }
        var clockService = _services.GetRequiredService<IClockService>();
        var worldClockService = _services.GetRequiredService<IWorldClockService>();
        var calendarService = _services.GetRequiredService<ICalendarService>();
        var scheduleService = _services.GetRequiredService<IScheduleService>();
        var todoService = _services.GetRequiredService<ITodoService>();
        var completionStyles = _services.GetRequiredService<CompletionStyleHub>();
        var timerEngine = _services.GetRequiredService<ITimerEngine>();
        var stopwatchEngine = _services.GetRequiredService<IStopwatchEngine>();
        var pomodoroService = _services.GetRequiredService<IPomodoroService>();
        var noiseMixer = _services.GetRequiredService<INoiseMixer>();
        var settingsStore = _services.GetRequiredService<ISettingsStore>();
        var hotkeyManager = _services.GetRequiredService<IHotkeyManager>();
        var hotkeyBindings = _services.GetRequiredService<HotkeyBindingService>();
        var clipboardRetention = _services.GetRequiredService<ClipboardRetentionService>();
        var clipboardIgnoreRules = _services.GetRequiredService<IgnoreRuleService>();
        var clipboardBoard = _services.GetRequiredService<ClipboardBoardService>();
        var clipboardFloat = _services.GetRequiredService<ClipboardFloatController>();
        var clipboardMonitor = _services.GetRequiredService<IClipboardMonitor>();
        _clipboardFloat = clipboardFloat;

        // P2-05：取色探针与历史色板。
        var colorProbe = _services.GetRequiredService<IColorProbe>();
        var colorHistory = _services.GetRequiredService<ColorHistoryService>();
        var clipboardWriter = _services.GetRequiredService<IClipboardWriter>();

        // P2-06：OCR（Windows.Media.Ocr）与截图服务。
        var ocrService = _services.GetRequiredService<IOcrService>();
        var screenCapture = _services.GetRequiredService<IScreenCaptureService>();

        // P2-07：截图历史与存储。
        var screenshotStore = _services.GetRequiredService<IScreenshotStore>();
        var focusStats = _services.GetRequiredService<IFocusStatsService>();
        var dailyGoals = _services.GetRequiredService<IDailyGoalService>();
        var countdownService = _services.GetRequiredService<ICountdownService>();
        var noteService = _services.GetRequiredService<INoteService>();
        var vaultService = _services.GetRequiredService<IVaultService>();
        _vault = vaultService;

        // P2-04 截图：选窗器在应用层注入（依赖 SystemAdapters 的窗口枚举）。
        var captureFlow = _services.GetRequiredService<CaptureFlowController>();
        captureFlow.WindowPicker = owner => Lumina.App.Capture.WindowPickerWindow.Pick(owner);
        _captureFlow = captureFlow;

        hotkeyManager.Pressed += OnHotkeyPressed;
        _pomodoro = pomodoroService;
        _hotkeyRegistration = _services.GetRequiredService<HotkeyRegistrationService>();
        var hotkeyStatus = RegisterGlobalHotkeys(_hotkeyRegistration);
        var reminderScheduler = _services.GetRequiredService<ReminderScheduler>();

        // 计时器/番茄钟到点提示：提前解析，确保订阅在窗口创建前就绪。
        var completionNotifier = _services.GetRequiredService<SessionCompletionNotifier>();

        var window = new MainWindow(
            themeManager,
            autoStartupService,
            widgetHost,
            clockService,
            worldClockService,
            calendarService,
            scheduleService,
            todoService,
            completionStyles,
            timerEngine,
            stopwatchEngine,
            pomodoroService,
            noiseMixer,
            settingsStore,
            hotkeyManager,
            hotkeyBindings,
            clipboardRetention,
            clipboardIgnoreRules,
            clipboardBoard,
            clipboardFloat,
            clipboardMonitor,
            captureFlow,
            colorProbe,
            colorHistory,
            clipboardWriter,
            ocrService,
            screenCapture,
            screenshotStore,
            focusStats,
            dailyGoals,
            countdownService,
            noteService,
            vaultService,
            _services.GetRequiredService<ISecureClipboard>(),
            reminderScheduler,
            completionNotifier,
            _services.GetRequiredService<INotificationSoundPlayer>(),
            hotkeyStatus,
            _services.GetRequiredService<IBackupCoordinator>());
        _mainWindow = window;
        window.Closing += OnMainWindowClosing;
        window.OnboardingRequested += (_, _) => ShowOnboarding(window);
        MainWindow = window;

        // 关闭主窗口只隐藏到托盘，进程必须继续存活；因此关停时机由托盘「退出程序」显式触发，
        // 不能交给「主窗口关闭」或「最后一个窗口关闭」的默认规则。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SubscribeHotkeyRecovery();

        // P4-02：先显示主窗口再注册托盘。托盘构造包含 Shell 通知登记与重试，
        // 实测约 1.4s；放在 Show 之前会把首帧一起拖慢，用户感知为“启动慢”。
        window.Show();
        _mainWindowShown = true;
        StartActivationListener();

        // P4-03：首帧看门狗放在后台线程轮询——O-02 类问题（进程存活但窗口不出现）下
        // UI 线程可能一并被渲染通道阻塞，只有独立线程才能留下证据并尝试兜底。
        window.ContentRendered += OnMainWindowContentRendered;
        _startupWatchdog = new StartupWatchdog(StartupWatchdog.ResolveBudget(), OnStartupWatchdogSignal);
        _startupWatchdog.Start();

        CreateTray(autoStartupService);
        if (_tray is not null)
        {
            _services.GetRequiredService<SystemNotifierRouter>().Attach(_tray);
        }

        // 托盘就绪后再提示热键占用，气泡才有承载对象。
        if (_hotkeyFailures.Count > 0)
        {
            Dispatcher.BeginInvoke(new Action(NotifyHotkeyFailures));
        }

        // P4-02：小组件实例化放到首帧之后的 Background 优先级，
        // 不与主窗口首帧抢 UI 线程；托盘/热键/提醒仍即时就绪。
        Dispatcher.BeginInvoke(
            new Action(() => RestoreWidgets(widgetHost)),
            System.Windows.Threading.DispatcherPriority.Background);
        reminderScheduler.Start();

        // P4-03：10 秒后台心跳用于判断真实休眠时长；休眠期间心跳不执行，间隔即休眠时间。
        _resumeCoordinator = new SystemResumeCoordinator(reminderScheduler);
        _resumeHeartbeat = new System.Threading.Timer(
            _ => _resumeCoordinator?.MarkHeartbeat(),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));

        StartClipboardMonitor();
        CheckProcessIntegrity();
    }

    /// <summary>
    /// 解锁、睡眠唤醒、显示器会话切换后系统可能丢失热键注册，
    /// 统一重注册并在失败时提示，避免“用着用着快捷键就没反应了”。
    /// </summary>
    private void SubscribeHotkeyRecovery()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitchForHotkeys;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChangedForHotkeys;

            // 保险库闲置计时以“应用内是否有输入”为准：在任意模块操作都算活跃。
            System.Windows.Input.InputManager.Current.PreProcessInput += OnGlobalInputForVault;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyInitializationFailed(_logger, ex);
            }
        }
    }

    private void OnSessionSwitchForHotkeys(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReRegisterHotkeys("解锁");
                _ = HandleSystemResumeAsync("解锁");
            }));
            return;
        }

        // 锁屏、断开会话、远程断开时立即清空保险库密钥。
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock
            or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect)
        {
            _vault?.Lock();
        }
    }

    private void OnPowerModeChangedForHotkeys(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReRegisterHotkeys("唤醒");
                _ = HandleSystemResumeAsync("唤醒");
            }));
            return;
        }

        // 睡眠即清空密钥，唤醒后必须重新输入主密码。
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            _vault?.Lock();
        }
    }

    private void OnGlobalInputForVault(object sender, System.Windows.Input.PreProcessInputEventArgs e) =>
        _vault?.NotifyActivity();

    /// <summary>
    /// 启动后自检进程完整性级别。Low（目录被低完整性标签降权）与 High（管理员启动）
    /// 都会让 UIPI 拦截托盘注册，必须在界面上说明原因并给出可执行的修复方式。
    /// </summary>
    private void CheckProcessIntegrity()
    {
        var level = ProcessIntegrity.Query();
        TrayService.WriteDiagnostic($"integrity={ProcessIntegrity.Describe(level)}");

        if (level is ProcessIntegrityLevel.Medium or ProcessIntegrityLevel.Unknown)
        {
            return;
        }

        if (_logger is not null)
        {
            AppLog.IntegrityWarning(_logger, ProcessIntegrity.Describe(level));
        }

        // 主窗口已显示，等首帧完成后再弹，避免被启动绘制吞掉。
        Dispatcher.BeginInvoke(new Action(() => ShowIntegrityWarning(level)));
    }

    private void ShowIntegrityWarning(ProcessIntegrityLevel level)
    {
        var directory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        var message = level == ProcessIntegrityLevel.High
            ? "检测到「拾光」以管理员权限运行。\n\n"
                + "Windows 的任务栏进程无法向高权限窗口发送消息，因此托盘图标不会显示，"
                + "系统通知也可能失效。\n\n"
                + "解决方法：关闭本窗口，直接双击程序启动，不要选择「以管理员身份运行」。"
            : $"检测到「拾光」运行在{ProcessIntegrity.Describe(level)}级别。\n\n"
                + "Windows 会阻止低完整性进程与任务栏通信，因此托盘图标不会显示，"
                + "系统通知与部分后台能力也会受限。\n\n"
                + "常见原因：程序所在文件夹（或其上级文件夹）被设置了低完整性标签，"
                + "例如从受限环境解压、复制，或被安全软件 / 沙箱标记。\n\n"
                + "解决方法（任选其一）：\n"
                + "1. 以当前用户身份打开 PowerShell，执行：\n"
                + $"   icacls \"{directory}\" /setintegritylevel \"(OI)(CI)M\"\n"
                + "   然后重新启动拾光。\n"
                + "2. 把整个程序文件夹复制到一个普通目录（例如 C:\\Apps\\Lumina）后再运行。\n\n"
                + "注意：不要以「管理员身份运行」拾光，高权限同样会导致托盘图标无法显示。";

        System.Windows.MessageBox.Show(
            _mainWindow,
            message,
            $"{AppPaths.ProductName} · 运行权限提示",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void StartClipboardMonitor()
    {
        try
        {
            _clipboardMonitor = _services?.GetService<IClipboardMonitor>();
            _clipboardMonitor?.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "剪贴板监听启动失败，采集功能暂不可用。");
        }
    }

    private bool InitializeDatabase()
    {
        try
        {
            return InitializeDatabaseCore();
        }
        catch (MigrationFailedException ex)
        {
            if (_logger is not null)
            {
                AppLog.MigrationFailed(_logger, ex.PreviousVersion, ex);
            }

            System.Windows.MessageBox.Show(
                $"数据库升级失败：{ex.Message}\n\n升级前备份：{ex.BackupPath ?? "无"}\n请查看日志后重试。",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        catch (Exception ex)
        {
            _databaseFailure = ex;
            if (_logger is not null)
            {
                AppLog.DatabaseInitializationFailed(_logger, ex);
            }

            // P4-04：库文件损坏时先给出“隔离原文件并重建空库”的选项，而不是直接退出。
            if (TryRecoverCorruptedDatabase(ex))
            {
                return true;
            }

            System.Windows.MessageBox.Show(
                $"数据库初始化失败：{ex.Message}\n\n日志：{AppPaths.LogFile}",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool InitializeDatabaseCore()
    {
        var result = _services!.GetRequiredService<DatabaseInitializer>().Initialize();
        if (_logger is not null)
        {
            AppLog.DatabaseReady(
                _logger,
                result.PreviousVersion,
                result.CurrentVersion,
                result.AppliedCount,
                result.BackupPath ?? "无",
                result.Fts5Available);
        }

        return true;
    }

    /// <summary>启动最早阶段应用待恢复请求；失败时只提示，不阻断启动。</summary>
    private void ApplyPendingRestore()
    {
        try
        {
            var processor = _services?.GetService<PendingRestoreProcessor>();
            if (processor is null)
            {
                return;
            }

            var outcome = processor.ApplyIfPending();
            if (!outcome.HadRequest)
            {
                return;
            }

            if (outcome.Applied)
            {
                if (_logger is not null)
                {
                    AppLog.PendingRestoreApplied(
                        _logger,
                        outcome.BackupPath ?? "未知",
                        outcome.SchemaVersion,
                        outcome.SafetyBackupPath ?? "无");
                }

                System.Windows.MessageBox.Show(
                    $"已从备份恢复数据：\n{outcome.BackupPath}\n\n"
                        + $"覆盖前快照：{outcome.SafetyBackupPath ?? "无"}",
                    AppPaths.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_logger is not null)
            {
                AppLog.PendingRestoreFailed(
                    _logger,
                    outcome.BackupPath ?? "未知",
                    outcome.Error ?? "未知错误");
            }

            System.Windows.MessageBox.Show(
                $"未能应用待恢复备份：{outcome.Error}",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // 待恢复请求的任何异常都只提示，不阻断启动。
            if (_logger is not null)
            {
                AppLog.PendingRestoreFailed(_logger, "未知", ex.Message);
            }
        }
    }

    /// <summary>
    /// 数据库无法打开且判定为损坏时：隔离原文件（不删除）并重建空库。
    /// 必须在用户明确确认后执行，且仅在启动早期、无业务连接时调用。
    /// </summary>
    private bool TryRecoverCorruptedDatabase(Exception cause)
    {
        var recovery = _services?.GetService<DatabaseRecoveryService>();
        if (recovery is null
            || !recovery.HasRecoverableDatabase
            || !DatabaseRecoveryService.IsCorruption(cause))
        {
            return false;
        }

        var answer = System.Windows.MessageBox.Show(
            "数据库文件疑似损坏，无法打开。\n\n"
                + "可以保留原文件并重建一个空库继续使用；原库会移入数据目录的 corrupt 子目录，"
                + "不会删除。重建后历史数据不会自动恢复，可从此前导出的备份恢复。\n\n"
                + "是否重建空库继续启动？",
            AppPaths.ProductName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            var quarantined = recovery.Quarantine();
            if (_logger is not null)
            {
                AppLog.DatabaseQuarantined(_logger, quarantined);
            }

            InitializeDatabaseCore();

            if (_logger is not null)
            {
                AppLog.DatabaseRebuilt(_logger, quarantined);
            }

            System.Windows.MessageBox.Show(
                $"已重建空数据库。\n\n原文件保留在：\n{quarantined}\n\n"
                    + "如需找回数据，可在设置 → 关于与数据 → 备份与恢复中选择此前导出的备份。",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.DatabaseInitializationFailed(_logger, ex);
            }

            System.Windows.MessageBox.Show(
                $"重建空数据库失败：{ex.Message}\n\n日志：{AppPaths.LogFile}",
                AppPaths.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void ReconcileFiles()
    {
        try
        {
            // 孤儿文件只报告不删除：用户可能手动把截图放进目录，误删不可恢复；
            // 截图记录的文件缺失则清理记录（文件已不存在，记录无意义）。
            var report = _services!.GetRequiredService<OrphanFileReconciler>()
                .Reconcile(deleteOrphanFiles: false);
            var screenshots = _services!.GetRequiredService<IScreenshotStore>().Reconcile();

            if (_logger is not null
                && (report.OrphanFiles.Count > 0 || report.MissingFiles.Count > 0))
            {
                AppLog.FilesReconciled(
                    _logger,
                    report.OrphanFiles.Count,
                    report.MissingFiles.Count,
                    report.DeletedFiles.Count);
            }

            if (_logger is not null && screenshots.MissingRecords > 0)
            {
                AppLog.ScreenshotsReconciled(_logger, screenshots.MissingRecords);
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.FileReconcileFailed(_logger, ex);
            }
        }
    }

    private string RegisterGlobalHotkeys(HotkeyRegistrationService registration)
    {
        try
        {
            var report = registration.RegisterAll();

            foreach (var entry in report.Entries)
            {
                if (entry.Status == HotkeyRegistrationStatus.Registered && _logger is not null)
                {
                    AppLog.HotkeyRegistered(_logger, $"{HotkeyActionCatalog.Title(entry.Action)} {entry.Gesture}");
                }
                else if (entry.Status is HotkeyRegistrationStatus.Conflict or HotkeyRegistrationStatus.Rejected
                    && _logger is not null)
                {
                    AppLog.HotkeyRegistrationFailed(
                        _logger,
                        $"{HotkeyActionCatalog.Title(entry.Action)} {entry.Gesture}");
                }
            }

            _hotkeyFailures = report.Failures
                .Select(entry => $"{HotkeyActionCatalog.Title(entry.Action)} {entry.Gesture}")
                .ToList();

            if (_logger is not null)
            {
                AppLog.HotkeySummary(_logger, report.Summary);
            }

            return report.Summary;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyInitializationFailed(_logger, ex);
            }

            return "全局热键不可用";
        }
    }

    /// <summary>
    /// 解锁、睡眠唤醒后系统可能丢弃热键注册，这里重注册并刷新状态；
    /// 失败只在日志与托盘提示里体现，不打断用户操作。
    /// </summary>
    private void ReRegisterHotkeys(string reason)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var registration = _services.GetRequiredService<HotkeyRegistrationService>();
            var report = registration.RegisterAll();

            if (_logger is not null)
            {
                AppLog.HotkeyReregistered(_logger, reason, report.Summary);
            }

            if (report.HasFailures)
            {
                NotifyHotkeyFailures();
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyInitializationFailed(_logger, ex);
            }
        }
    }

    /// <summary>注册失败时用托盘气泡给出可见提示，点击跳到设置中心的热键区。</summary>
    private void NotifyHotkeyFailures()
    {
        if (_hotkeyFailures.Count == 0 || _services is null)
        {
            return;
        }

        var body = string.Join("；", _hotkeyFailures);
        _ = _services.GetRequiredService<SystemNotifierRouter>()
            .ShowToastAsync("部分全局快捷键未生效", $"{body}（可能被其他程序占用，可在设置中心改键）", "settings");
    }

    private void CreateTray(IAutoStartupService autoStartupService)
    {
        try
        {
            var settings = _services?.GetService<ISettingsStore>();
            _tray = new TrayService(
                autoStartupService,
                ShowMainWindow,
                ExitApplication,
                OnNotificationClicked,
                status =>
                {
                    if (_logger is not null)
                    {
                        AppLog.TrayStatus(_logger, status);
                    }
                },
                () => _vault?.Lock(),
                () => _vault is { IsConfigured: true, IsLocked: false },
                settings is null
                    ? null
                    : () => settings.Get(
                        PreferenceKeys.ClipboardPaused,
                        PreferenceDefaults.ClipboardPaused),
                settings is null
                    ? null
                    : paused => settings.Set(
                        PreferenceKeys.ClipboardPaused,
                        paused,
                        module: "clipboard"));

            if (_logger is not null)
            {
                AppLog.TrayReady(
                    _logger,
                    $"{_tray.IconSource} / {_tray.RegistrationStatus}",
                    _tray.IsIconVisible);
            }
        }
        catch (Exception ex)
        {
            TrayService.WriteDiagnostic($"托盘构造失败：{ex.GetType().Name}: {ex.Message}");
            if (_logger is not null)
            {
                AppLog.TrayInitializationFailed(_logger, ex);
            }

            _tray = null;
        }
    }

    private void OnNotificationClicked(string? moduleId)
    {
        ShowMainWindow();

        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            _mainWindow?.NavigateTo(moduleId);
        }
    }

    private void StartActivationListener()
    {
        try
        {
            _activation = new SingleInstanceActivation(
                () => Dispatcher.BeginInvoke(new Action(ShowMainWindow)));
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.ActivationInitializationFailed(_logger, ex);
            }

            _activation = null;
        }
    }

    private void RestoreWidgets(IWidgetHost widgetHost)
    {
        try
        {
            widgetHost.RestoreVisible();
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.WidgetInitializationFailed(_logger, ex);
            }
        }
    }

    private void OnHotkeyPressed(string action)
    {
        // 先记“按键已收到”，与后续流程成功与否解耦，便于排查“按了没反应”。
        if (_logger is not null && HotkeyRegistrationService.IsActiveGlobalAction(action))
        {
            AppLog.HotkeyActionInvoked(_logger, HotkeyActionCatalog.Title(action));
        }

        switch (action)
        {
            case HotkeyActions.ShowWindow:
                Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                break;

            case HotkeyActions.NewNote:
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowMainWindow();
                    _mainWindow?.CreateNote();
                }));
                break;

            case HotkeyActions.OpenClipboard:
                Dispatcher.BeginInvoke(new Action(() => _clipboardFloat?.Toggle()));
                break;

            case HotkeyActions.TogglePomodoro:
                Dispatcher.BeginInvoke(new Action(TogglePomodoro));
                break;

            case HotkeyActions.RegionCapture:
                Dispatcher.BeginInvoke(new Action(async () => await RunCaptureAsync(
                    HotkeyActions.RegionCapture,
                    () => _captureFlow!.RunRegionAsync())));
                break;

            case HotkeyActions.FullScreenCapture:
                Dispatcher.BeginInvoke(new Action(async () => await RunCaptureAsync(
                    HotkeyActions.FullScreenCapture,
                    () => _captureFlow!.RunFullScreenAsync())));
                break;

            case HotkeyActions.DelayedCapture:
                Dispatcher.BeginInvoke(new Action(async () => await RunCaptureAsync(
                    HotkeyActions.DelayedCapture,
                    () => _captureFlow!.RunDelayedAsync())));
                break;
        }
    }

    /// <summary>
    /// 全局热键触发的截图：主窗口隐藏时按窗口隐藏语义处理，
    /// 截图完成后编辑器留在前台；流程内部的“已有任务”提示不弹窗，避免打断当前操作。
    /// </summary>
    private async Task RunCaptureAsync(string action, Func<Task<string>> flow)
    {
        if (_captureFlow is null)
        {
            return;
        }

        try
        {
            var message = await flow().ConfigureAwait(true);

            // 取消与“已有任务进行中”都属于正常结果，不做额外提示。
            _ = message;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyActionFailed(_logger, HotkeyActionCatalog.Title(action), ex);
            }

            NotifyHotkeyActionFailed(HotkeyActionCatalog.Title(action), ex.Message);
        }
    }

    /// <summary>与计时器页一致的三态切换：运行中暂停、暂停恢复、空闲时按设定时长开始。</summary>
    private void TogglePomodoro()
    {
        if (_pomodoro is null)
        {
            return;
        }

        try
        {
            var state = _pomodoro.Current;

            switch (state.Phase)
            {
                case PomodoroPhase.Focus:
                case PomodoroPhase.Break:
                    _pomodoro.Pause();
                    break;

                case PomodoroPhase.Paused:
                    _pomodoro.Resume();
                    break;

                default:
                    // 快捷键启动沿用用户在番茄钟页设置的专注时长，避免与界面显示不一致。
                    var settings = _services?.GetService<ISettingsStore>();
                    var focusSeconds = settings?.Get(
                        PreferenceKeys.PomodoroTargetSeconds,
                        PreferenceDefaults.PomodoroTargetSeconds) ?? PreferenceDefaults.PomodoroTargetSeconds;

                    if (focusSeconds <= 0)
                    {
                        focusSeconds = PreferenceDefaults.PomodoroTargetSeconds;
                    }

                    var focus = TimeSpan.FromSeconds(focusSeconds);
                    var cycles = state.Completed
                        ? Math.Max(1, state.TotalCycles)
                        : TimerInputPolicy.DefaultCycles;

                    _pomodoro.Start(focus, PomodoroTimingPolicy.RestFor(focus), cycles);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyActionFailed(_logger, HotkeyActionCatalog.Title(HotkeyActions.TogglePomodoro), ex);
            }
        }
    }

    private void NotifyHotkeyActionFailed(string title, string message)
    {
        if (_services is null)
        {
            return;
        }

        _ = _services.GetRequiredService<SystemNotifierRouter>()
            .ShowToastAsync($"「{title}」执行失败", message);
    }

    private void ShowMainWindow()
    {
        _mainWindow?.ShowFromTray();
    }

    private void ExitApplication()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        UnregisterHotkeys();

        if (_mainWindow is not null)
        {
            // Closing 会因 _exitRequested 直接放行；随后显式关停进程（ShutdownMode 为 OnExplicitShutdown）。
            _mainWindow.Close();
        }

        Shutdown(0);
    }

    /// <summary>退出、注销前注销全部全局热键，避免系统内残留占用。</summary>
    private void UnregisterHotkeys()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitchForHotkeys;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChangedForHotkeys;
            System.Windows.Input.InputManager.Current.PreProcessInput -= OnGlobalInputForVault;
            _hotkeyRegistration?.UnregisterAll();
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.HotkeyInitializationFailed(_logger, ex);
            }
        }
    }

    protected override void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs e)
    {
        // Windows 注销/关机时不得把窗口关闭拦截为“隐藏到托盘”。
        _exitRequested = true;
        UnregisterHotkeys();
        base.OnSessionEnding(e);
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        // 托盘不可用就没有恢复入口：此时必须真正结束进程，避免留下无界面的“幽灵进程”。
        if (_tray is null || !_tray.IsIconVisible)
        {
            _exitRequested = true;
            Dispatcher.BeginInvoke(new Action(() => Shutdown(0)));
            return;
        }

        e.Cancel = true;

        // 主流桌面软件（微信 / QQ）语义：关闭窗口 = 隐藏到托盘，任务栏按钮随之消失，
        // 托盘图标保持常驻；进程与后台服务（提醒、剪贴板采集、热键）继续运行。
        _mainWindow?.Hide();
        _tray.ShowMinimizedTip();

        if (_logger is not null)
        {
            AppLog.HiddenToTray(_logger);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _clipboardMonitor?.Stop();
        _clipboardMonitor = null;

        _captureFlow?.CloseAllPins();
        _captureFlow = null;

        _vault?.Lock();
        _vault = null;

        _services?.GetService<ReminderScheduler>()?.Stop();
        _services?.GetService<SystemNotifierRouter>()?.Detach();

        _startupWatchdog?.Dispose();
        _startupWatchdog = null;

        _resumeHeartbeat?.Dispose();
        _resumeHeartbeat = null;
        _resumeCoordinator = null;

        _activation?.Dispose();
        _activation = null;

        _tray?.Dispose();
        _tray = null;

        _mainWindow = null;

        if (_logger is not null)
        {
            AppLog.Exit(_logger, AppPaths.ProductName, e.ApplicationExitCode);
        }

        _services?.Dispose();
        _services = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private void OnMainWindowContentRendered(object? sender, EventArgs e)
    {
        var window = sender as MainWindow;
        if (window is not null)
        {
            window.ContentRendered -= OnMainWindowContentRendered;
        }

        _startupWatchdog?.MarkFirstFrame();

        // 兼容性探测包含 WinRT OCR 与 COM 音频枚举，放到首帧之后，不占用启动预算。
        Dispatcher.BeginInvoke(new Action(CaptureCompatibility), DispatcherPriority.Background);

        // P4-06：首次运行引导在首帧之后非模态显示，不阻塞主界面。
        if (window is not null)
        {
            Dispatcher.BeginInvoke(
                new Action(() => ShowOnboardingIfNeeded(window)),
                DispatcherPriority.Background);
        }
    }

    /// <summary>全新安装显示首次引导；升级用户（数据库已存在）只补一次“已完成”标记。</summary>
    private void ShowOnboardingIfNeeded(MainWindow window)
    {
        if (_services is null)
        {
            return;
        }

        var settings = _services.GetRequiredService<ISettingsStore>();
        var completed = settings.Get(
            PreferenceKeys.OnboardingCompleted,
            PreferenceDefaults.OnboardingCompleted);
        LogOnboarding($"检查 completed={completed} existing={_isExistingInstall}");

        if (!completed && _isExistingInstall)
        {
            settings.Set(PreferenceKeys.OnboardingCompleted, true, module: "migration");
            completed = true;
        }

        if (!completed)
        {
            ShowOnboarding(window);
            return;
        }

        ShowDataRootNoticeOnce();
    }

    /// <summary>显示（或重新显示）首次引导；关闭后按需跳转密码管理器。</summary>
    private void ShowOnboarding(MainWindow window)
    {
        if (_services is null)
        {
            return;
        }

        if (_onboarding is { IsVisible: true })
        {
            _onboarding.Activate();
            return;
        }

        var onboarding = new FirstRunWindow(
            window,
            _services.GetRequiredService<ThemeManager>(),
            _services.GetRequiredService<IAutoStartupService>(),
            _services.GetRequiredService<ISettingsStore>(),
            _services.GetRequiredService<IVaultService>(),
            _dataRootNotice);

        onboarding.Closed += (_, _) =>
        {
            _onboarding = null;
            if (onboarding.OpenVaultRequested)
            {
                window.NavigateTo("vault");
            }
        };

        _onboarding = onboarding;
        onboarding.Show();
        LogOnboarding($"已显示 visible={onboarding.IsVisible}");

        // 提示已在引导窗口内展示，不再额外弹窗。
        _dataRootNotice = null;
    }

    private void LogOnboarding(string detail)
    {
        if (_logger is not null)
        {
            AppLog.Onboarding(_logger, detail);
        }
    }

    /// <summary>数据目录回退提示：引导已完成时用一次性弹窗告知本次会话的替代位置。</summary>
    private void ShowDataRootNoticeOnce()
    {
        var notice = _dataRootNotice;
        if (notice is null)
        {
            return;
        }

        _dataRootNotice = null;
        System.Windows.MessageBox.Show(
            notice,
            AppPaths.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool DatabaseExists()
    {
        try
        {
            var info = new FileInfo(AppPaths.DatabaseFile);
            return info.Exists && info.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>采集并记录运行环境能力；成功一次后不再重复，失败最多重试两次。</summary>
    private void CaptureCompatibility()
    {
        if (_compatibilityCaptured || _compatibilityAttempts >= 3)
        {
            return;
        }

        _compatibilityAttempts++;

        try
        {
            var probe = _services?.GetService<ICompatibilityProbe>();
            if (probe is null)
            {
                return;
            }

            _compatibility = probe.Capture();
            _compatibilityCaptured = true;
            if (_logger is not null)
            {
                AppLog.CompatibilitySnapshot(_logger, _compatibility.Describe());
            }

            _mainWindow?.SetCompatibility(_compatibility);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "兼容性探测失败（第 {Attempt} 次），稍后重试", _compatibilityAttempts);

            // 失败后保留重试机会，避免诊断卡永久停在“探测中”。
            if (_compatibilityAttempts < 3)
            {
                var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                retry.Tick += (_, _) =>
                {
                    retry.Stop();
                    CaptureCompatibility();
                };
                retry.Start();
            }
        }
    }

    private void OnStartupWatchdogSignal(StartupWatchdogSignal signal)
    {
        if (_logger is null)
        {
            return;
        }

        switch (signal.State)
        {
            case StartupWatchdogState.Rendered:
                if (signal.Detail == "late frame ignored")
                {
                    return;
                }

                AppLog.FirstFrameReady(_logger, signal.Elapsed.TotalMilliseconds, signal.Detail);
                break;

            case StartupWatchdogState.Degraded:
                AppLog.StartupRenderDegraded(_logger, signal.Elapsed.TotalMilliseconds, signal.Attempt);
                Dispatcher.BeginInvoke(new Action(ApplySoftwareRendering));
                break;

            case StartupWatchdogState.Failed:
                AppLog.StartupRenderFailed(_logger, signal.Elapsed.TotalMilliseconds);
                break;
        }
    }

    /// <summary>渲染通道卡住时的兜底：切 WPF 软件渲染并强制重排，绕开 GPU 合成。</summary>
    private void ApplySoftwareRendering()
    {
        if (System.Windows.Media.RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly)
        {
            return;
        }

        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.InvalidateVisual();
        _mainWindow.UpdateLayout();
    }

    /// <summary>唤醒/解锁后的恢复：补扫提醒并把小组件收敛回可见区域。</summary>
    private async Task HandleSystemResumeAsync(string reason)
    {
        if (_resumeCoordinator is null)
        {
            return;
        }

        try
        {
            var outcome = await _resumeCoordinator.HandleResumeAsync(reason);
            if (_logger is not null)
            {
                AppLog.SystemResumed(
                    _logger,
                    outcome.Reason,
                    Math.Round(outcome.DormantFor.TotalSeconds, 1),
                    outcome.RemindersFired,
                    outcome.RemindersRescanned);
            }

            if (_services?.GetService<IWidgetHost>() is WidgetHost widgets)
            {
                widgets.ConvergeVisibleWidgets();
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLog.SystemResumeFailed(_logger, reason, ex);
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.DispatcherUnhandled(_logger, e.Exception);
        }
        else
        {
            WriteEmergencyLog("UI 线程未处理异常（启动早期，日志未就绪）", e.Exception);
        }

        var startupFailure = !_mainWindowShown;
        System.Windows.MessageBox.Show(
            startupFailure
                ? $"启动失败：{e.Exception.Message}\n\n应用将退出。"
                : $"发生未处理错误：{e.Exception.Message}\n\n详细信息已写入日志，可在设置中打开日志目录。",
            AppPaths.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // 启动阶段失败必须退出，避免“进程活着但无窗口”的静默状态；
        // 主窗口显示后的 UI 异常则保持进程存活，避免后台服务整体退出。
        e.Handled = true;
        if (startupFailure)
        {
            Shutdown(1);
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        if (_logger is not null)
        {
            AppLog.DomainUnhandled(_logger, e.IsTerminating, exception);
        }
        else
        {
            WriteEmergencyLog($"进程级未处理异常（终止={e.IsTerminating}）", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnobservedTask(_logger, e.Exception);
        }
        else
        {
            WriteEmergencyLog("未观察的任务异常", e.Exception);
        }

        e.SetObserved();
    }

    /// <summary>日志系统就绪前的应急落盘；失败时静默放弃，绝不二次抛出。</summary>
    private static void WriteEmergencyLog(string message, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(AppPaths.Root, "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-crash.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}{exception}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch (Exception)
        {
            // 应急日志不可写时放弃，避免在异常处理路径上再次抛出。
        }
    }
}
