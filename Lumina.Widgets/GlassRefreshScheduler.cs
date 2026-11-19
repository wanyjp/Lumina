using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Lumina.Services.Widgets;

namespace Lumina.Widgets;

/// <summary>
/// 玻璃层采样节拍：静止时 200ms（5fps）省电；检测到光标移动、按键拖动、
/// 前台窗口移动/缩放，或背景画面本身在变化（视频、滚动、动画）时切到 33ms（30fps），
/// 之后约 1.2 秒没有活动再自动回落到低频。节拍决策由 <see cref="GlassSamplingPolicy"/> 负责。
/// </summary>
internal sealed class GlassRefreshScheduler : IDisposable
{
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;

    private readonly DispatcherTimer _timer;
    private readonly Action _refresh;
    private readonly GlassSamplingPolicy _policy = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _cursorX;
    private int _cursorY;
    private bool _hasCursor;
    private IntPtr _foreground;
    private NativeRect _foregroundRect;
    private bool _hasForegroundRect;
    private bool _disposed;

    public GlassRefreshScheduler(Action refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        _refresh = refresh;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(GlassSamplingPolicy.ActiveIntervalMs),
        };
        _timer.Tick += OnTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        ResetBaseline();
        _policy.Reset();
        _policy.Advance(_clock.ElapsedMilliseconds, activity: false);
        _timer.Interval = TimeSpan.FromMilliseconds(_policy.CurrentIntervalMs);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    /// <summary>背景帧比对发现画面在动时调用，用于把节拍抬到高频。</summary>
    public void NotifyActivity()
    {
        _policy.NotifyActivity(_clock.ElapsedMilliseconds);
        if (_timer.IsEnabled && _timer.Interval.TotalMilliseconds > GlassSamplingPolicy.ActiveIntervalMs)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(GlassSamplingPolicy.ActiveIntervalMs);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var interval = _policy.Advance(_clock.ElapsedMilliseconds, DetectActivity());
        if (Math.Abs(_timer.Interval.TotalMilliseconds - interval) > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(interval);
        }

        _refresh();
    }

    private void ResetBaseline()
    {
        _hasCursor = false;
        _foreground = IntPtr.Zero;
        _hasForegroundRect = false;
    }

    private bool DetectActivity()
    {
        var active = false;

        if (GetCursorPos(out var point))
        {
            if (_hasCursor
                && (Math.Abs(point.X - _cursorX) > 1 || Math.Abs(point.Y - _cursorY) > 1))
            {
                active = true;
            }

            _cursorX = point.X;
            _cursorY = point.Y;
            _hasCursor = true;
        }

        // 按住鼠标基本等于在拖窗口、拖滚动条或框选，直接抬到高频。
        if ((GetAsyncKeyState(VkLeftButton) & 0x8000) != 0
            || (GetAsyncKeyState(VkRightButton) & 0x8000) != 0)
        {
            active = true;
        }

        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            if (foreground != _foreground)
            {
                _foreground = foreground;
                _hasForegroundRect = GetWindowRect(foreground, out _foregroundRect);
                active = true;
            }
            else if (GetWindowRect(foreground, out var rect))
            {
                if (_hasForegroundRect
                    && (rect.Left != _foregroundRect.Left
                        || rect.Top != _foregroundRect.Top
                        || rect.Right != _foregroundRect.Right
                        || rect.Bottom != _foregroundRect.Bottom))
                {
                    active = true;
                }

                _foregroundRect = rect;
                _hasForegroundRect = true;
            }
        }

        return active;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

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
}
