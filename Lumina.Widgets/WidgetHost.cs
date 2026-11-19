using System.Windows;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>
/// 小组件宿主：创建/更新/关闭小组件窗口，并按实例持久化位置、尺寸、
/// 透明度、置顶与点击穿透状态；拖动/缩放后的写入做 300ms 防抖。
/// </summary>
public sealed class WidgetHost : IWidgetHost, IDisposable
{
    private const string ClockStateKey = "widget.clock.state.v1";
    private const int PersistDebounceMilliseconds = 300;

    private readonly ISettingsStore _settings;
    private readonly IClockService _clock;
    private readonly IWorldClockService _worldClock;
    private readonly ICalendarService _calendar;
    private readonly IScheduleService _schedules;
    private readonly ITodoService _todos;
    private readonly ITimerEngine _timer;
    private readonly IStopwatchEngine _stopwatch;
    private readonly IPomodoroService _pomodoro;
    private readonly ICountdownService _countdowns;
    private readonly INoteService _notes;
    private readonly Dictionary<WidgetKind, WidgetWindowBase> _windows = [];
    private readonly IDisposable _settingsSubscription;
    private DispatcherTimer? _persistTimer;
    private bool _disposed;
    private bool _disposing;

    public WidgetHost(
        ISettingsStore settings,
        IClockService clock,
        IWorldClockService worldClock,
        ICalendarService calendar,
        IScheduleService schedules,
        ITodoService todos,
        ITimerEngine timer,
        IStopwatchEngine stopwatch,
        IPomodoroService pomodoro,
        ICountdownService countdowns,
        INoteService notes)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(worldClock);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stopwatch);
        ArgumentNullException.ThrowIfNull(pomodoro);
        ArgumentNullException.ThrowIfNull(countdowns);
        ArgumentNullException.ThrowIfNull(notes);

        _settings = settings;
        _clock = clock;
        _worldClock = worldClock;
        _calendar = calendar;
        _schedules = schedules;
        _todos = todos;
        _timer = timer;
        _stopwatch = stopwatch;
        _pomodoro = pomodoro;
        _countdowns = countdowns;
        _notes = notes;

        _settingsSubscription = _settings.Changed.Subscribe(new DensityObserver(OnSettingChanged));
    }

    /// <summary>计时小组件点击「返回主窗口」：由上层显示主窗口。</summary>
    public event Action? ReturnToMainRequested;

    /// <summary>计时类小组件（计时器/秒表/番茄钟）。</summary>
    private static bool IsTimerWidget(WidgetKind kind) =>
        kind is WidgetKind.Timer or WidgetKind.Stopwatch or WidgetKind.Pomodoro;

    private static bool IsSupported(WidgetKind kind) =>
        kind is WidgetKind.Clock or WidgetKind.Schedule or WidgetKind.Countdown
            or WidgetKind.Note || IsTimerWidget(kind);

    public void ShowWidget(WidgetState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        state = NormalizeState(state);

        if (!IsSupported(state.Kind))
        {
            throw new NotSupportedException($"当前未实现小组件 {state.Kind}。");
        }

        var window = _windows.GetValueOrDefault(state.Kind);
        if (window is null)
        {
            window = CreateWindow(state);
            _windows[state.Kind] = window;
            window.Show();
        }
        else
        {
            window.ApplyState(state);
            if (window is CountdownWidgetWindow countdownWindow)
            {
                countdownWindow.ApplySource(state.SourceId);
            }

            if (window is NoteWidgetWindow noteWindow)
            {
                noteWindow.ApplySource(state.SourceId);
            }

            if (!window.IsVisible)
            {
                window.Show();
            }
        }

        window.FrameCaptureEnabled = FrameCaptureEnabled;
        SaveState(state);
        _settings.Set(VisibilityKey(state.Kind), true, module: "widget");
    }

    /// <summary>更新状态并持久化；未打开的组件不因设置变更被强制显示。</summary>
    public void UpdateWidget(WidgetState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        state = NormalizeState(state);

        if (!IsSupported(state.Kind))
        {
            throw new NotSupportedException($"当前未实现小组件 {state.Kind}。");
        }

        if (_windows.TryGetValue(state.Kind, out var window))
        {
            window.ApplyState(state);
            if (window is CountdownWidgetWindow countdownWindow)
            {
                countdownWindow.ApplySource(state.SourceId);
            }

            if (window is NoteWidgetWindow noteWindow)
            {
                noteWindow.ApplySource(state.SourceId);
            }
        }

        SaveState(state);
    }

    private WidgetWindowBase CreateWindow(WidgetState state)
    {
        if (state.Kind == WidgetKind.Clock)
        {
            return new ClockWidgetWindow(
                _clock,
                _worldClock,
                _settings,
                state,
                OnWindowStateChanged,
                OnWindowClosed);
        }

        if (state.Kind == WidgetKind.Countdown)
        {
            return new CountdownWidgetWindow(
                _countdowns,
                state,
                OnWindowStateChanged,
                OnWindowClosed);
        }

        if (state.Kind == WidgetKind.Note)
        {
            return new NoteWidgetWindow(
                _notes,
                state,
                OnWindowStateChanged,
                OnWindowClosed);
        }

        if (state.Kind == WidgetKind.Schedule)
        {
            return new ScheduleWidgetWindow(
                _calendar,
                _schedules,
                _todos,
                state,
                OnWindowStateChanged,
                OnWindowClosed);
        }

        var mode = state.Kind switch
        {
            WidgetKind.Stopwatch => TimerWidgetMode.Stopwatch,
            WidgetKind.Pomodoro => TimerWidgetMode.Pomodoro,
            _ => TimerWidgetMode.Timer,
        };

        return new TimerWidgetWindow(
            _timer,
            _stopwatch,
            _pomodoro,
            _settings,
            mode,
            state,
            CompactTimerWidget,
            OnWindowStateChanged,
            OnWindowClosed,
            () => ReturnToMainRequested?.Invoke());
    }

    /// <summary>计时类小组件是否使用紧凑密度。</summary>
    private bool CompactTimerWidget => _settings.Get(
        PreferenceKeys.TimerWidgetCompact,
        PreferenceDefaults.TimerWidgetCompact);

    /// <summary>玻璃层是否使用 WGC 逐帧捕获；只对亚克力/液态玻璃生效。</summary>
    private bool FrameCaptureEnabled => _settings.Get(
        PreferenceKeys.WidgetGlassCapture,
        PreferenceDefaults.WidgetGlassCapture);

    private void OnSettingChanged(SettingChanged change)
    {
        if (change.Key == PreferenceKeys.WidgetGlassCapture)
        {
            var capture = FrameCaptureEnabled;
            foreach (var window in _windows.Values)
            {
                window.FrameCaptureEnabled = capture;
            }

            return;
        }

        if (change.Key != PreferenceKeys.TimerWidgetCompact)
        {
            return;
        }

        var compact = CompactTimerWidget;
        foreach (var (kind, window) in _windows)
        {
            if (IsTimerWidget(kind) && window is TimerWidgetWindow timerWindow)
            {
                timerWindow.ApplyCompact(compact);
            }
        }
    }

    public void CloseWidget(WidgetKind kind)
    {
        _settings.Set(VisibilityKey(kind), false, module: "widget");

        var window = _windows.GetValueOrDefault(kind);
        if (window is null)
        {
            return;
        }

        SaveState(window.State);
        window.Close();
    }

    public void RestoreVisible()
    {
        foreach (var kind in new[]
                 {
                     WidgetKind.Clock, WidgetKind.Timer, WidgetKind.Stopwatch,
                     WidgetKind.Pomodoro, WidgetKind.Schedule, WidgetKind.Countdown, WidgetKind.Note,
                 })
        {
            if (_settings.Get<bool>(VisibilityKey(kind), kind == WidgetKind.Clock))
            {
                ShowWidget(GetState(kind));
            }
        }
    }

    public void PersistLayout() => FlushLayout();

    /// <summary>
    /// 唤醒、解锁或显示器变化后，把所有已显示小组件收敛到当前屏幕工作区。
    /// 单个窗口失败不影响其余窗口。
    /// </summary>
    public void ConvergeVisibleWidgets()
    {
        foreach (var window in _windows.Values)
        {
            try
            {
                window.ConvergeToVisibleArea();
            }
            catch (InvalidOperationException)
            {
                // 窗口正在关闭或尚未完成初始化：下一次显示时会重新收敛。
            }
        }
    }

    public WidgetState GetState(WidgetKind kind)
    {
        var window = _windows.GetValueOrDefault(kind);
        if (window is not null)
        {
            return window.State;
        }

        return LoadState(kind);
    }

    public bool IsVisible(WidgetKind kind) =>
        _windows.TryGetValue(kind, out var window) && window.IsVisible;

    private WidgetState LoadState(WidgetKind kind)
    {
        var saved = _settings.Get<WidgetState>(StateKey(kind));
        return saved is not null && saved.Kind == kind
            ? NormalizeState(saved)
            : DefaultState(kind);
    }

    private WidgetState NormalizeState(WidgetState state)
    {
        state = state with { Skin = WidgetSkinCatalog.Normalize(state.Skin) };

        if (state.Kind == WidgetKind.Schedule)
        {
            return state with
            {
                Width = Math.Max(360, state.Width),
                Height = Math.Max(300, state.Height),
            };
        }

        if (state.Kind == WidgetKind.Countdown)
        {
            return state with { Width = 260, Height = 220 };
        }

        if (IsTimerWidget(state.Kind))
        {
            var mode = state.Kind switch
            {
                WidgetKind.Stopwatch => TimerWidgetMode.Stopwatch,
                WidgetKind.Pomodoro => TimerWidgetMode.Pomodoro,
                _ => TimerWidgetMode.Timer,
            };
            var size = TimerWidgetWindow.PreferredSize(mode, CompactTimerWidget);
            return state with { Width = size.Width, Height = size.Height };
        }

        return state;
    }

    private static string VisibilityKey(WidgetKind kind) =>
        $"widget.{kind.ToString().ToLowerInvariant()}.visible.v1";

    private void SaveState(WidgetState state)
    {
        _settings.Set(StateKey(state.Kind), state, module: "widget");
    }

    private void OnWindowStateChanged(WidgetState state) => SchedulePersist();

    /// <summary>拖动/缩放期间只在 300ms 无新变更后落库，避免高频写配置。</summary>
    private void SchedulePersist()
    {
        if (_disposed)
        {
            return;
        }

        if (_persistTimer is null)
        {
            _persistTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PersistDebounceMilliseconds),
            };
            _persistTimer.Tick += (_, _) => FlushLayout();
        }

        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void FlushLayout()
    {
        _persistTimer?.Stop();

        foreach (var window in _windows.Values)
        {
            SaveState(window.State);
        }
    }

    private void OnWindowClosed(WidgetKind kind)
    {
        var window = _windows.GetValueOrDefault(kind);
        if (window is not null)
        {
            SaveState(window.State);
            _windows.Remove(kind);
        }

        if (!_disposing)
        {
            _settings.Set(VisibilityKey(kind), false, module: "widget");
        }
    }

    private static string StateKey(WidgetKind kind) =>
        kind == WidgetKind.Clock
            ? ClockStateKey
            : $"widget.{kind.ToString().ToLowerInvariant()}.state.v1";

    private WidgetState DefaultState(WidgetKind kind)
    {
        if (kind == WidgetKind.Clock)
        {
            const double clockWidth = 300;
            const double clockHeight = 180;
            var workArea = SystemParameters.WorkArea;
            return new WidgetState(
                WidgetKind.Clock,
                X: workArea.Right - clockWidth - 24,
                Y: workArea.Bottom - clockHeight - 24,
                Width: clockWidth,
                Height: clockHeight,
                Opacity: 1.0,
                Topmost: true,
                ClickThrough: false);
        }

        if (kind is WidgetKind.Timer or WidgetKind.Stopwatch or WidgetKind.Pomodoro)
        {
            var compact = _settings.Get(
                PreferenceKeys.TimerWidgetCompact,
                PreferenceDefaults.TimerWidgetCompact);
            var mode = kind switch
            {
                WidgetKind.Stopwatch => TimerWidgetMode.Stopwatch,
                WidgetKind.Pomodoro => TimerWidgetMode.Pomodoro,
                _ => TimerWidgetMode.Timer,
            };
            var size = TimerWidgetWindow.PreferredSize(mode, compact);
            var width = size.Width;
            var height = size.Height;
            var workArea = SystemParameters.WorkArea;
            var index = kind switch
            {
                WidgetKind.Stopwatch => 1,
                WidgetKind.Pomodoro => 2,
                _ => 0,
            };

            return new WidgetState(
                kind,
                X: workArea.Right - width - 24,
                Y: workArea.Bottom - height - 24 - index * (height + 16),
                Width: width,
                Height: height,
                Opacity: 1.0,
                Topmost: true,
                ClickThrough: false);
        }

        if (kind == WidgetKind.Countdown)
        {
            const double countdownWidth = 260;
            const double countdownHeight = 220;
            var workArea = SystemParameters.WorkArea;
            return new WidgetState(
                WidgetKind.Countdown,
                X: workArea.Right - countdownWidth - 24,
                Y: workArea.Top + 80,
                Width: countdownWidth,
                Height: countdownHeight,
                Opacity: 1.0,
                Topmost: true,
                ClickThrough: false);
        }

        if (kind == WidgetKind.Note)
        {
            const double noteWidth = 300;
            const double noteHeight = 340;
            var workArea = SystemParameters.WorkArea;
            return new WidgetState(
                WidgetKind.Note,
                X: workArea.Right - noteWidth - 24,
                Y: workArea.Top + 320,
                Width: noteWidth,
                Height: noteHeight,
                Opacity: 1.0,
                Topmost: true,
                ClickThrough: false);
        }

        if (kind == WidgetKind.Schedule)
        {
            const double scheduleWidth = 380;
            const double scheduleHeight = 320;
            var workArea = SystemParameters.WorkArea;
            return new WidgetState(
                WidgetKind.Schedule,
                X: workArea.Right - scheduleWidth - 24,
                Y: workArea.Bottom - scheduleHeight - 420,
                Width: scheduleWidth,
                Height: scheduleHeight,
                Opacity: 1.0,
                Topmost: true,
                ClickThrough: false);
        }

        throw new NotSupportedException($"当前未实现小组件 {kind}。");
    }

    /// <summary>设置流观察者：密度切换需要即时刷新已打开的计时类小组件。</summary>
    private sealed class DensityObserver(Action<SettingChanged> handler) : IObserver<SettingChanged>
    {
        public void OnNext(SettingChanged value) => handler(value);

        public void OnError(Exception error)
        {
            // 设置流异常不影响小组件展示。
        }

        public void OnCompleted()
        {
            // 无生命周期结束语义。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _persistTimer?.Stop();
        foreach (var window in _windows.Values)
        {
            SaveState(window.State);
        }

        _disposing = true;
        _disposed = true;
        _settingsSubscription.Dispose();
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }
}
