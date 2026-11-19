using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Lumina.App.Shell;

/// <summary>
/// 托盘生命周期：显示主窗口、自启开关、系统通知与退出。
/// 关闭主窗口只是隐藏到托盘；只有右键菜单「退出程序」才真正结束进程。
/// <para>
/// 直接调用 Win32 <c>Shell_NotifyIcon</c> 而非 WinForms <c>NotifyIcon</c>：后者只暴露
/// 一个内部 <c>Visible</c> 标志，无法确认 Shell 是否真的接受了 NIM_ADD。本实现记录
/// NIM_ADD 返回值与 <c>Shell_NotifyIconGetRect</c> 自检结果、失败自动重试、
/// Explorer 重启后自动重建，并把状态写入 <c>logs/tray-status.log</c>。
/// </para>
/// <para>
/// 注册形态刻意保持最保守：普通隐藏窗口（非 HWND_MESSAGE）+ 非 GUID 的 hWnd/uID 组合。
/// message-only 窗口与 <c>NIF_GUID</c> 在部分 Windows 11 环境会被 Shell 拒绝，且 GUID
/// 会沿用历史“隐藏”偏好，导致图标既不进任务栏也不进溢出菜单。
/// </para>
/// </summary>
internal sealed class TrayService : IDisposable, INotifier
{
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;

    private const uint NotifyIconVersion4 = 0x00000004;

    /// <summary>非 GUID 图标标识，与 hWnd 组合唯一确定本进程的托盘图标。</summary>
    private const int TrayIconId = 1;

    private const uint NiifInfo = 0x00000001;

    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 0x1F;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int NinBalloonUserClick = 0x0405;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(
        ref NotifyIconIdentifier identifier,
        out NativeRect iconLocation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private readonly IAutoStartupService _autoStartupService;
    private readonly Action _showWindow;
    private readonly Action _exitApplication;
    private readonly Action<string?>? _notificationClicked;
    private readonly Action<string>? _statusChanged;
    private readonly Action? _lockVault;
    private readonly Func<bool>? _canLockVault;
    private readonly Func<bool>? _isClipboardPaused;
    private readonly Action<bool>? _setClipboardPaused;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _autoStartItem;
    private readonly Forms.ToolStripMenuItem _lockVaultItem;
    private readonly Forms.ToolStripMenuItem _pauseClipboardItem;
    private readonly SynchronizationContext? _syncContext;
    private readonly System.Windows.Threading.DispatcherTimer _retryTimer;
    private readonly HwndSource _window;
    private readonly Drawing.Icon _icon;
    private readonly uint _taskbarCreated;
    private readonly int _retryLimit = 3;

    private string? _pendingRoute;
    private Forms.NotifyIcon? _fallback;
    private bool _tipShown;
    private bool _disposed;
    private bool _added;
    private int _attempts;
    private long _lastShowTicks;

    /// <summary>图标来源，写入启动日志便于诊断。</summary>
    public string IconSource { get; }

    /// <summary>Shell 是否确认接受了图标，或已切到 WinForms 回落实现。</summary>
    public bool IsIconVisible => !_disposed && (_added || _fallback is not null);

    /// <summary>最近一次注册状态描述，写入日志供排障。</summary>
    public string RegistrationStatus { get; private set; } = "pending";

    public TrayService(
        IAutoStartupService autoStartupService,
        Action showWindow,
        Action exitApplication,
        Action<string?>? notificationClicked = null,
        Action<string>? statusChanged = null,
        Action? lockVault = null,
        Func<bool>? canLockVault = null,
        Func<bool>? isClipboardPaused = null,
        Action<bool>? setClipboardPaused = null)
    {
        ArgumentNullException.ThrowIfNull(autoStartupService);
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _autoStartupService = autoStartupService;
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        _notificationClicked = notificationClicked;
        _statusChanged = statusChanged;
        _lockVault = lockVault;
        _canLockVault = canLockVault;
        _isClipboardPaused = isClipboardPaused;
        _setClipboardPaused = setClipboardPaused;
        _syncContext = SynchronizationContext.Current;

        WriteDiagnostic(
            $"construct: pid={Environment.ProcessId}, root={AppPaths.Root}, base={AppContext.BaseDirectory}");

        _autoStartItem = new Forms.ToolStripMenuItem("开机自启") { CheckOnClick = false };
        _autoStartItem.Click += OnAutoStartClick;
        _lockVaultItem = new Forms.ToolStripMenuItem("锁定保险库");
        _lockVaultItem.Click += OnLockVaultClick;
        _pauseClipboardItem = new Forms.ToolStripMenuItem("暂停剪贴板采集");
        _pauseClipboardItem.Click += OnPauseClipboardClick;
        _menu = BuildMenu(_autoStartItem);

        var (icon, source) = LoadAppIcon();
        _icon = icon;
        IconSource = source;

        var parameters = new HwndSourceParameters("LuminaTray")
        {
            // 普通顶层窗口（不显示、位于屏幕外），而非 HWND_MESSAGE：message-only 窗口
            // 在部分 Windows 11 配置下会被 Shell 拒绝，且收不到 TaskbarCreated 广播。
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
        };
        parameters.SetSize(1, 1);
        parameters.SetPosition(-32000, -32000);
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);

        WriteDiagnostic(
            $"hwnd=0x{_window.Handle.ToInt64():X}, hicon=0x{_icon.Handle.ToInt64():X}, source={IconSource}");

        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        EnsureRegistered();

        _retryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _retryTimer.Tick += OnRetryTick;
        if (!_added)
        {
            _retryTimer.Start();
        }
        else
        {
            ShowTrayHintOnce();
        }
    }

    private Forms.ContextMenuStrip BuildMenu(Forms.ToolStripMenuItem autoStartItem)
    {
        var menu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("打开主窗口");
        showItem.Click += (_, _) => RequestShow();
        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(_lockVaultItem);
        menu.Items.Add(_pauseClipboardItem);

        menu.Items.Add(autoStartItem);

        var visibilityItem = new Forms.ToolStripMenuItem("托盘图标显示设置…");
        visibilityItem.Click += (_, _) => OpenTaskbarSettings();
        menu.Items.Add(visibilityItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        var exitItem = new Forms.ToolStripMenuItem("退出程序");
        exitItem.Click += (_, _) => _exitApplication();
        menu.Items.Add(exitItem);

        menu.Opening += OnMenuOpening;
        return menu;
    }

    private NotifyIconData BuildData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = TrayIconId,
        uFlags = flags,
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _icon.Handle,
        szTip = "拾光 · Lumina",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
        dwInfoFlags = 0,
        uTimeoutOrVersion = 0,
        dwState = 0,
        dwStateMask = 0,
        guidItem = Guid.Empty,
        hBalloonIcon = IntPtr.Zero,
    };

    /// <summary>向 Shell 注册图标；返回是否已被接受。</summary>
    private bool EnsureRegistered()
    {
        if (_disposed)
        {
            return false;
        }

        if (_added)
        {
            return true;
        }

        _attempts++;
        var data = BuildData(NifMessage | NifIcon | NifTip);
        var accepted = Shell_NotifyIcon(NimAdd, ref data);
        var error = accepted ? 0 : Marshal.GetLastWin32Error();

        if (accepted)
        {
            _added = true;

            // 版本 4 才会收到 NIN_SELECT / NIN_KEYSELECT 等现代回调；NIF_SHOWTIP 也只能在
            // NIM_SETVERSION 阶段使用，放进 NIM_ADD 属未定义行为（旧实现即如此）。
            data.uTimeoutOrVersion = NotifyIconVersion4;
            var versionAccepted = Shell_NotifyIcon(NimSetVersion, ref data);

            var presence = QueryIconPresence();
            RegistrationStatus =
                $"NIM_ADD ok (uID={TrayIconId}, cbSize={data.cbSize}, attempt={_attempts}, "
                + $"v4={(versionAccepted ? "ok" : "failed")}, {presence})";
            _statusChanged?.Invoke(RegistrationStatus);
            WriteDiagnostic(RegistrationStatus);
            return true;
        }

        RegistrationStatus =
            $"NIM_ADD failed (error={error}, attempt={_attempts}, {QueryIconPresence()})";
        _statusChanged?.Invoke(RegistrationStatus);
        WriteDiagnostic(RegistrationStatus);
        return false;
    }

    /// <summary>
    /// 托盘状态单独落一份短文件，不依赖通用日志管道：托盘故障常发生在启动极早期，
    /// 且用户环境里日志可能因文件占用写不进去，这份快照是排障的第一手证据。
    /// </summary>
    internal static void WriteDiagnostic(string line)
    {
        var text = $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}";

        // 同时写数据目录与程序目录：数据目录可能被重定向或不可写，程序目录是最后的取证点。
        foreach (var directory in new[] { Path.Combine(AppPaths.Root, "logs"), AppContext.BaseDirectory })
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "tray-status.log"), text);
            }
            catch (Exception)
            {
                // 诊断写入失败不得影响托盘本身。
            }
        }
    }

    /// <summary>
    /// 询问 Shell 是否真的持有该 GUID 的托盘图标。NIM_ADD 返回 TRUE 只说明调用被受理；
    /// 图标若被策略或 Windows 11 的显示偏好彻底关闭，GetRect 会失败——这是区分
    /// “没注册”和“注册了但被隐藏”的关键证据。Shell 建图标是异步的，因此最多重试 3 次。
    /// </summary>
    private string QueryIconPresence()
    {
        var identifier = new NotifyIconIdentifier
        {
            cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
            hWnd = _window.Handle,
            uID = TrayIconId,
            guidItem = Guid.Empty,
        };

        var hr = unchecked((int)0x80004005);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                hr = Shell_NotifyIconGetRect(ref identifier, out var rect);
                if (hr == 0)
                {
                    return $"GetRect ok ({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
                }

                if (hr != unchecked((int)0x80070490))
                {
                    // 不是“找不到图标”，无需重试。
                    break;
                }
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
                return "GetRect unavailable";
            }

            Thread.Sleep(60);
        }

        return $"GetRect hr=0x{hr:X8}";
    }

    private void OnRetryTick(object? sender, EventArgs e)
    {
        if (_added || _disposed)
        {
            _retryTimer.Stop();
            return;
        }

        if (EnsureRegistered())
        {
            _retryTimer.Stop();
            ShowTrayHintOnce();
        }
        else if (_attempts >= _retryLimit)
        {
            _retryTimer.Stop();
            ActivateWinFormsFallback();
        }
    }

    /// <summary>
    /// 自定义 NIM_ADD 在部分受限会话中会被系统拒绝。为避免托盘完全失效，
    /// 重试耗尽后回落到 WinForms NotifyIcon（与 P2-04 之前一致的实现）。
    /// </summary>
    private void ActivateWinFormsFallback()
    {
        if (_fallback is not null || _disposed)
        {
            return;
        }

        try
        {
            var icon = new Forms.NotifyIcon
            {
                Text = "拾光 · Lumina",
                Icon = _icon,
                ContextMenuStrip = _menu,
                Visible = true,
            };
            icon.DoubleClick += (_, _) => RequestShow();
            icon.MouseClick += (_, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left)
                {
                    RequestShow();
                }
            };
            icon.BalloonTipClicked += (_, _) => OnBalloonTipClicked();
            _fallback = icon;

            RegistrationStatus = "NIM_ADD 被拒绝，已回落 WinForms NotifyIcon";
            _statusChanged?.Invoke(RegistrationStatus);
            WriteDiagnostic($"{RegistrationStatus}, fallbackVisible={icon.Visible}");
            ShowTrayHintOnce();
        }
        catch (Exception ex)
        {
            RegistrationStatus = $"WinForms 回落失败：{ex.GetType().Name}";
            _statusChanged?.Invoke(RegistrationStatus);
            WriteDiagnostic($"{RegistrationStatus}: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && message == unchecked((int)_taskbarCreated) && !_disposed
            && _fallback is null)
        {
            // Explorer 重启后所有托盘图标失效，必须重新 NIM_ADD。
            _added = false;
            _attempts = 0;
            if (EnsureRegistered() && !_retryTimer.IsEnabled)
            {
                ShowTrayHintOnce();
            }
            else if (!_added)
            {
                _retryTimer.Start();
            }

            return IntPtr.Zero;
        }

        if (message != TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var notification = unchecked((int)lParam.ToInt64()) & 0xFFFF;
        switch (notification)
        {
            case WmLButtonUp:
            case WmLButtonDblClk:
            case NinSelect:
            case NinKeySelect:
                // 左键单击即打开主窗口（微信 / QQ 行为）；一次单击会连发 WM_LBUTTONUP 与
                // NIN_SELECT，由 RequestShow 合并，避免重复 Activate 造成闪烁。
                RequestShow();
                handled = true;
                break;
            case WmRButtonUp:
            case WmContextMenu:
                ShowMenu();
                handled = true;
                break;
            case NinBalloonUserClick:
                OnBalloonTipClicked();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 单击托盘图标会依次收到 WM_LBUTTONUP 与 NIN_SELECT，双击还会再来一轮；
    /// 这里做 250ms 合并，避免同一手势重复 Activate 造成窗口闪烁。
    /// </summary>
    private void RequestShow()
    {
        var now = Environment.TickCount64;
        if (now - _lastShowTicks < 250)
        {
            return;
        }

        _lastShowTicks = now;
        _showWindow();
    }

    private void ShowMenu()
    {
        try
        {
            // 先让隐藏窗口成为前台，菜单才能在点击外部时正常收起。
            SetForegroundWindow(_window.Handle);
            _menu.Show(Forms.Cursor.Position);
        }
        catch (Exception)
        {
            // 菜单弹出失败不影响托盘常驻。
        }
    }

    /// <summary>
    /// Windows 11 默认把新标识的托盘图标收进"隐藏的图标"区，应用无法强制固定。
    /// 首次启动给出一次性指引，并写入标记文件避免重复打扰。
    /// </summary>
    private void ShowTrayHintOnce()
    {
        var marker = Path.Combine(AppPaths.Root, "tray-hint-v2.flag");
        if (File.Exists(marker))
        {
            return;
        }

        try
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(marker, DateTimeOffset.Now.ToString("O"));
            ShowBalloon(
                6000,
                "拾光已启动",
                "左键单击托盘图标可打开主窗口，右键可打开菜单（退出程序等）。"
                    + "若任务栏未显示拾光图标，请点开托盘的 ^（隐藏的图标），把「拾光」拖到托盘；"
                    + "也可右键托盘图标选择「托盘图标显示设置…」。",
                null);
        }
        catch (Exception)
        {
            // 标记或气泡失败都不影响托盘常驻。
        }
    }

    private static void OpenTaskbarSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 系统设置页打开失败不影响托盘菜单其余功能。
        }
    }

    /// <summary>系统通知（Windows 10/11 以气泡形式展示）；失败返回 false。</summary>
    public Task<bool> ShowToastAsync(
        string title,
        string body,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(false);
        }

        var context = _syncContext;
        if (context is null || context == SynchronizationContext.Current)
        {
            return Task.FromResult(ShowBalloon(5000, title, body, route));
        }

        var completion = new TaskCompletionSource<bool>();
        context.Post(
            _ => completion.TrySetResult(ShowBalloon(5000, title, body, route)),
            null);
        return completion.Task;
    }

    private bool ShowBalloon(int timeoutMs, string title, string body, string? route)
    {
        if (_fallback is not null)
        {
            try
            {
                _pendingRoute = route;
                _fallback.ShowBalloonTip(timeoutMs, title, body, Forms.ToolTipIcon.Info);
                return true;
            }
            catch (Exception)
            {
                _pendingRoute = null;
                return false;
            }
        }

        if (!EnsureRegistered())
        {
            return false;
        }

        try
        {
            _pendingRoute = route;
            var data = BuildData(NifInfo);
            data.szInfo = body ?? string.Empty;
            data.szInfoTitle = title ?? string.Empty;
            data.uTimeoutOrVersion = (uint)timeoutMs;
            data.dwInfoFlags = NiifInfo;
            var accepted = Shell_NotifyIcon(NimModify, ref data);
            if (!accepted)
            {
                _pendingRoute = null;
            }

            return accepted;
        }
        catch (Exception)
        {
            _pendingRoute = null;
            return false;
        }
    }

    private void OnBalloonTipClicked()
    {
        var route = _pendingRoute;
        _pendingRoute = null;

        try
        {
            _notificationClicked?.Invoke(route);
        }
        catch (Exception)
        {
            // 跳转失败不得中断托盘进程。
        }
    }

    /// <summary>
    /// 托盘图标优先取应用内嵌的 lumina.ico（品牌图标、含多尺寸），
    /// 失败再退到 exe 关联图标与系统占位图标。返回来源名供启动日志诊断。
    /// </summary>
    private static (Drawing.Icon Icon, string Source) LoadAppIcon()
    {
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/lumina.ico", UriKind.Absolute));
            if (streamInfo?.Stream is { } stream)
            {
                using (stream)
                {
                    var icon = new Drawing.Icon(stream);
                    if (icon.Handle != IntPtr.Zero)
                    {
                        return (icon, "embedded:Assets/lumina.ico");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            // 资源缺失或图标损坏：继续回落。
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return (icon, "exe:ExtractAssociatedIcon");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            // 回退到系统占位图标。
        }

        // SystemIcons.Application 是共享句柄：克隆一份交给曾在退出时释放的调用方。
        return ((Icon)Drawing.SystemIcons.Application.Clone(), "system:Application");
    }

    public void ShowMinimizedTip()
    {
        if (_tipShown || _disposed)
        {
            return;
        }

        _tipShown = true;
        ShowBalloon(
            3000,
            "拾光仍在后台运行",
            "窗口已关闭到托盘，左键单击托盘图标可重新打开；右键菜单可退出程序。",
            null);
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var canLock = _canLockVault?.Invoke() == true;

            // 保险库未创建或已锁定时直接隐藏，避免菜单里出现不可点的灰项。
            _lockVaultItem.Visible = canLock;
            _lockVaultItem.Enabled = canLock;
            _lockVaultItem.ToolTipText = canLock ? "立即清空内存中的保险库密钥" : string.Empty;
        }
        catch (Exception ex)
        {
            _lockVaultItem.Visible = false;
            _lockVaultItem.Enabled = false;
            _lockVaultItem.ToolTipText = $"无法读取保险库状态：{ex.Message}";
        }

        var pauseAvailable = _isClipboardPaused is not null && _setClipboardPaused is not null;
        _pauseClipboardItem.Visible = pauseAvailable;
        if (pauseAvailable)
        {
            try
            {
                var paused = _isClipboardPaused!.Invoke();
                _pauseClipboardItem.Checked = paused;
                _pauseClipboardItem.Text = paused ? "恢复剪贴板采集" : "暂停剪贴板采集";
            }
            catch (Exception)
            {
                // 配置读取失败时保留可点状态，交由点击路径给出明确错误。
                _pauseClipboardItem.Checked = false;
                _pauseClipboardItem.Text = "暂停剪贴板采集";
            }
        }

        try
        {
            _autoStartItem.Checked = _autoStartupService.IsEnabled();
            _autoStartItem.ToolTipText = "以系统实际状态为准";
        }
        catch (Exception ex)
        {
            _autoStartItem.Checked = false;
            _autoStartItem.ToolTipText = $"无法读取自启状态：{ex.Message}";
        }
    }

    private void OnLockVaultClick(object? sender, EventArgs e)
    {
        try
        {
            _lockVault?.Invoke();
            _statusChanged?.Invoke("保险库已锁定");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"锁定保险库失败：{ex.Message}",
                AppPaths.ProductName,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnPauseClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            var paused = _isClipboardPaused?.Invoke() == true;
            _setClipboardPaused?.Invoke(!paused);
            _statusChanged?.Invoke(paused ? "已恢复剪贴板采集" : "已暂停剪贴板采集");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"切换剪贴板采集失败：{ex.Message}",
                AppPaths.ProductName,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnAutoStartClick(object? sender, EventArgs e)
    {
        try
        {
            var enabled = !_autoStartupService.IsEnabled();
            _autoStartupService.SetEnabled(enabled);
            _autoStartItem.Checked = _autoStartupService.IsEnabled();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"开机自启设置失败：{ex.Message}",
                "拾光",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            TryRefreshAutoStartState();
        }
    }

    private void TryRefreshAutoStartState()
    {
        try
        {
            _autoStartItem.Checked = _autoStartupService.IsEnabled();
        }
        catch (Exception)
        {
            _autoStartItem.Checked = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _retryTimer.Stop();

        if (_added)
        {
            var data = BuildData(0);
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        _window.RemoveHook(WndProc);
        _window.Dispose();
        _fallback?.Dispose();
        _fallback = null;
        _menu.Dispose();
        _icon.Dispose();
    }
}
