using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using Lumina.Services.Backup;
using Lumina.Services.Capture;
using Lumina.Services.Clipboard;
using Lumina.Services.Colors;
using Lumina.Services.Compatibility;
using Lumina.Services.Completion;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Notifications;
using Lumina.Services.Reminders;
using Lumina.UI.Wpf.Capture;
using Lumina.UI.Wpf.Clipboard;
using Lumina.UI.Wpf.Shell;
using Lumina.UI.Wpf.Themes;

namespace Lumina.App;

/// <summary>
/// P0-06 无边框主窗口：自定义标题栏（最小化/最大化/关闭）、九模块导航壳与关闭到托盘语义。
/// 关闭主窗口只隐藏窗口并保留托盘图标（任务栏按钮随之消失），进程继续在后台运行；
/// 真正结束进程的入口只有托盘右键菜单「退出程序」。
/// </summary>
public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    public MainWindow(
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
        InitializeComponent();

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        var shell = new MainShellView(
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
            settings,
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
            ocr,
            captureService,
            screenshotStore,
            focusStats,
            dailyGoals,
            countdownService,
            noteService,
            vaultService,
            secureClipboard,
            reminderScheduler,
            completionNotifier,
            notificationSound,
            hotkeyStatus,
            backupCoordinator);

        // 缩小为小组件后主窗口自动退到任务栏，避免遮挡小组件。
        shell.WidgetMiniRequested += (_, _) => MinimizeForWidget();
        shell.OnboardingRequested += (_, _) => OnboardingRequested?.Invoke(this, EventArgs.Empty);
        Root.Content = shell;

        StateChanged += OnWindowStateChanged;
    }

    /// <summary>设置页请求重新打开首次运行引导。</summary>
    public event EventHandler? OnboardingRequested;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
    }

    /// <summary>把启动时采集的兼容性快照转交设置中心展示。</summary>
    public void SetCompatibility(CompatibilitySnapshot snapshot)
    {
        if (Root.Content is MainShellView shell)
        {
            shell.SetCompatibility(snapshot);
        }
    }

    /// <summary>按模块 id 跳转，供托盘通知点击与提醒条“查看”使用。</summary>
    public void NavigateTo(string moduleId)
    {
        if (Root.Content is MainShellView shell)
        {
            shell.NavigateTo(moduleId);
        }
    }

    /// <summary>全局热键新建便签：切到便签页并打开编辑窗。</summary>
    public void CreateNote()
    {
        if (Root.Content is MainShellView shell)
        {
            shell.CreateNote();
        }
    }

    /// <summary>从托盘、热键或第二实例信号唤起窗口（隐藏状态会重新显示并还原）。</summary>
    public void ShowFromTray()
    {
        // 关闭到托盘后窗口处于 Hide 状态；先还原最小化状态，再显示，避免任务栏只出现最小化按钮。
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>缩小为桌面小组件后把主窗口最小化，避免遮住小组件。</summary>
    public void MinimizeForWidget()
    {
        if (WindowState != WindowState.Minimized)
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // 一律走 Closing：由 App 决定隐藏到托盘还是真正退出。
        Close();
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        RootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    /// <summary>
    /// 无边框窗口默认按“整屏”最大化会盖住任务栏；这里把最大化尺寸收敛到当前显示器工作区。
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var target = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = info.Work;
        var screen = info.Monitor;

        target.MaxPosition.X = work.Left - screen.Left;
        target.MaxPosition.Y = work.Top - screen.Top;
        target.MaxSize.X = work.Right - work.Left;
        target.MaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(target, lParam, fDeleteOld: true);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
