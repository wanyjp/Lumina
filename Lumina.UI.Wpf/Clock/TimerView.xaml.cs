using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>计时器子页签。</summary>
public enum TimerMode
{
    Timer,
    Stopwatch,
    Pomodoro,
}

/// <summary>
/// P1-03 计时器 / 秒表 / 番茄钟三合一视图。
/// 状态由引擎持有，本视图只负责输入、展示与控制；全屏与缩小小组件走真实窗口。
/// </summary>
public partial class TimerView : UserControl
{
    private readonly ITimerEngine _timer;
    private readonly IStopwatchEngine _stopwatch;
    private readonly IPomodoroService _pomodoro;
    private readonly IWidgetHost _widgetHost;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _refresh;

    private TimerMode _mode = TimerMode.Timer;

    /// <summary>计时器与番茄钟各自记忆的时长：两个页签不再共用一个输入框数值。</summary>
    private TimeSpan _timerTarget = TimeSpan.FromMinutes(25);

    private TimeSpan _pomodoroTarget = TimeSpan.FromMinutes(25);

    private TimerFullscreenWindow? _fullscreen;
    private int _renderedMarkCount = -1;

    /// <summary>已缩小为小组件：由外层主窗口负责最小化，避免遮挡小组件。</summary>
    public event EventHandler? MiniRequested;

    public TimerView(
        ITimerEngine timer,
        IStopwatchEngine stopwatch,
        IPomodoroService pomodoro,
        IWidgetHost widgetHost,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stopwatch);
        ArgumentNullException.ThrowIfNull(pomodoro);
        ArgumentNullException.ThrowIfNull(widgetHost);
        ArgumentNullException.ThrowIfNull(settings);

        _timer = timer;
        _stopwatch = stopwatch;
        _pomodoro = pomodoro;
        _widgetHost = widgetHost;
        _settings = settings;

        InitializeComponent();

        _timerTarget = ReadStoredTarget(PreferenceKeys.TimerTargetSeconds);
        _pomodoroTarget = ReadStoredTarget(PreferenceKeys.PomodoroTargetSeconds);

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refresh.Tick += OnRefresh;

        // 页签在 XAML 加载完成后再选中，避免 Checked 事件在命名元素初始化前触发。
        TimerTab.IsChecked = true;
        ApplyTarget(_timerTarget);
        ApplyModeVisibility();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 点击卡片空白等不可获焦区域时 TextBox 不会触发 LostFocus，
        // 这里统一补一次提交，保证删掉数字后“点别处”一定会补足两位。
        PreviewMouseDown += OnPreviewMouseDown;
        UpdateDisplay();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refresh.Start();
        UpdateDisplay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refresh.Stop();
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        UpdateDisplay();
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var next = tag switch
        {
            "stopwatch" => TimerMode.Stopwatch,
            "pomodoro" => TimerMode.Pomodoro,
            _ => TimerMode.Timer,
        };

        if (next == _mode)
        {
            ApplyModeVisibility();
            UpdateDisplay();
            return;
        }

        // 切换页签：先把当前页签的输入落到自己的时长，再载入目标页签的记忆值。
        CommitTargetForMode();
        _mode = next;
        LoadTargetForMode();

        ApplyModeVisibility();
        UpdateDisplay();
    }

    private void ApplyModeVisibility()
    {
        TimerSetupPanel.Visibility = _mode == TimerMode.Stopwatch ? Visibility.Collapsed : Visibility.Visible;
        TimerPresetsPanel.Visibility = _mode == TimerMode.Timer ? Visibility.Visible : Visibility.Collapsed;
        PomodoroCyclesPanel.Visibility = _mode == TimerMode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        DurationCaption.Text = _mode == TimerMode.Pomodoro ? "专注时长" : "目标时长";

        var listMode = _mode != TimerMode.Pomodoro;
        LapPanel.Visibility = listMode ? Visibility.Visible : Visibility.Collapsed;
        LapButton.Visibility = _mode == TimerMode.Stopwatch ? Visibility.Visible : Visibility.Collapsed;
        MarkButton.Visibility = _mode == TimerMode.Timer ? Visibility.Visible : Visibility.Collapsed;

        LapTitleText.Text = _mode == TimerMode.Timer ? "标记" : "圈次";
        LapSplitTitle.Text = _mode == TimerMode.Timer ? "间隔 / 已过" : "单圈 / 累计";

        if (_mode == TimerMode.Timer)
        {
            RefreshTimerMarks();
        }
    }

    private void UpdateDisplay()
    {
        switch (_mode)
        {
            case TimerMode.Stopwatch:
                UpdateStopwatch();
                break;
            case TimerMode.Pomodoro:
                UpdatePomodoro();
                break;
            default:
                UpdateTimer();
                break;
        }
    }

    private void UpdateTimer()
    {
        var state = _timer.State;
        PhaseText.Text = state.IsRunning ? "计时中" : state.IsPaused ? "已暂停" : "待开始";
        DisplayText.Text = state.IsRunning || state.IsPaused
            ? FormatTimer(state.Remaining)
            : FormatTimer(CurrentTarget());
        PlayPauseButton.Content = state.IsRunning ? "暂停" : state.IsPaused ? "继续" : "开始";
        HintText.Text = "以结束时刻为基准计时，暂停后不会漂移；时间到会在应用内和系统托盘提醒。";

        // 标记可能存在小组件里新增：数量变化即重建列表，避免主界面显示滞后。
        SyncTimerMarks();
    }

    private void SyncTimerMarks()
    {
        if (_timer.Marks.Count == _renderedMarkCount)
        {
            return;
        }

        RefreshTimerMarks();
    }

    private void UpdateStopwatch()
    {
        var state = _stopwatch.State;
        PhaseText.Text = state.IsRunning ? "计时中" : state.IsPaused ? "已暂停" : "待开始";
        DisplayText.Text = FormatStopwatch(state.Elapsed);
        PlayPauseButton.Content = state.IsRunning ? "暂停" : state.IsPaused ? "继续" : "开始";
        HintText.Text = $"已计 {_stopwatch.Laps.Count} 圈；计圈记录本次累计时间，可随时重置。";
        RefreshLaps();
    }

    private void UpdatePomodoro()
    {
        var state = _pomodoro.Current;
        PhaseText.Text = state.Phase switch
        {
            PomodoroPhase.Focus => $"专注中 · 第 {state.Cycle} / {state.TotalCycles} 轮",
            PomodoroPhase.Break => $"休息中 · 第 {state.Cycle} / {state.TotalCycles} 轮",
            PomodoroPhase.Paused => $"已暂停 · 第 {state.Cycle} / {state.TotalCycles} 轮",
            _ when state.Completed => $"已完成全部 {state.TotalCycles} 轮 · 可再次开始",
            _ => "待开始",
        };

        DisplayText.Text = state.Phase == PomodoroPhase.Idle
            ? FormatTimer(CurrentTarget())
            : FormatTimer(state.Remaining);
        PlayPauseButton.Content = state.Phase switch
        {
            PomodoroPhase.Focus or PomodoroPhase.Break => "暂停",
            PomodoroPhase.Paused => "继续",
            _ => "开始",
        };

        HintText.Text = state.Completed
            ? "全部轮次已完成；点击开始会按当前专注时长开启新一轮。"
            : $"专注 {FormatTimer(CurrentTarget())}，休息 {PomodoroTimingPolicy.DescribeRest(CurrentTarget())}"
              + "（按专注时长的 20% 自动计算，最长 5 分钟）；每轮结束都会提醒。";
    }

    private void RefreshLaps()
    {
        var laps = _stopwatch.Laps;
        LapList.ItemsSource = laps
            .Select((total, index) => new
            {
                Index = index + 1,
                Split = index == 0 ? total : total - laps[index - 1],
                Total = total,
            })
            .Select(item => new
            {
                item.Index,
                Split = FormatStopwatch(item.Split),
                Total = FormatStopwatch(item.Total),
            })
            .ToArray();

        EmptyLapText.Text = "还没有计圈记录。";
        EmptyLapText.Visibility = laps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case TimerMode.Stopwatch:
                {
                    var state = _stopwatch.State;
                    if (state.IsRunning)
                    {
                        _stopwatch.Pause();
                    }
                    else
                    {
                        _stopwatch.Start();
                    }

                    break;
                }

            case TimerMode.Pomodoro:
                {
                    var state = _pomodoro.Current;
                    if (state.Phase == PomodoroPhase.Paused)
                    {
                        _pomodoro.Resume();
                    }
                    else if (state.Phase is PomodoroPhase.Focus or PomodoroPhase.Break)
                    {
                        _pomodoro.Pause();
                    }
                    else
                    {
                        if (!TryReadTarget(out var focus))
                        {
                            break;
                        }

                        _pomodoro.Start(
                            focus,
                            PomodoroTimingPolicy.RestFor(focus),
                            SelectedCycles());
                        if (_settings.Get<bool>(
                                PreferenceKeys.PomodoroFullscreen,
                                PreferenceDefaults.PomodoroFullscreen))
                        {
                            OpenFullscreen();
                        }
                    }

                    break;
                }

            default:
                {
                    var state = _timer.State;
                    if (state.IsRunning)
                    {
                        _timer.Pause();
                    }
                    else if (state.IsPaused)
                    {
                        _timer.Resume();
                    }
                    else if (TryReadTarget(out var target))
                    {
                        _timer.Start(target);
                    }
                    else
                    {
                        HintText.Text = "请输入大于 0 的目标时长。";
                    }

                    break;
                }
        }

        UpdateDisplay();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case TimerMode.Stopwatch:
                _stopwatch.Reset();
                break;
            case TimerMode.Pomodoro:
                _pomodoro.ForceStop();
                break;
            default:
                _timer.Reset();
                break;
        }

        UpdateDisplay();
    }

    private void OnLapClick(object sender, RoutedEventArgs e)
    {
        _stopwatch.MarkLap();
        UpdateDisplay();
    }

    /// <summary>
    /// 计时器标记：记录当前已过时间，便于分段时间点，不改变计时状态。
    /// 标记存放在引擎里，主界面与小组件共用，重置时一并清空。
    /// </summary>
    private void OnMarkClick(object sender, RoutedEventArgs e)
    {
        var state = _timer.State;
        if (!state.IsRunning && !state.IsPaused)
        {
            HintText.Text = "计时开始后才能标记时间点。";
            return;
        }

        _timer.Mark();
        RefreshTimerMarks();
        UpdateDisplay();
    }

    private void RefreshTimerMarks()
    {
        var marks = _timer.Marks;

        LapList.ItemsSource = marks
            .Select((total, index) => new
            {
                Index = index + 1,
                Split = index == 0 ? total : total - marks[index - 1],
                Total = total,
            })
            .Select(item => new
            {
                item.Index,
                Split = FormatTimer(item.Split),
                Total = FormatTimer(item.Total),
            })
            .ToArray();

        EmptyLapText.Text = "还没有标记。计时过程中点击「标记」记录时间点。";
        EmptyLapText.Visibility = marks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _renderedMarkCount = marks.Count;
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string minutes } || !int.TryParse(minutes, out var value))
        {
            return;
        }

        var total = TimeSpan.FromMinutes(value);
        ApplyTarget(total);
        StoreTarget(total);
        _timer.Reset();
        UpdateDisplay();
    }

    /// <summary>
    /// 时长输入确认：先把每个输入框补足两位（删空 → 00，删成 1 → 01），
    /// 再归一化分钟/秒溢出（75 分 → 1:15:00），最后写入当前页签自己的时长。
    /// </summary>
    private void OnDurationCommit(object sender, RoutedEventArgs e)
    {
        if (_mode == TimerMode.Stopwatch)
        {
            return;
        }

        NormalizeDurationBoxes();

        if (!TimerInputPolicy.TryParse(HourBox.Text, MinuteBox.Text, SecondBox.Text, out var target))
        {
            HintText.Text = "目标时长需为非负整数，且总和大于 0。";
            return;
        }

        ApplyTarget(target);
        CommitTargetForMode();
        UpdateDisplay();
    }

    private void OnDurationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        OnDurationCommit(sender, e);
    }

    /// <summary>焦点还在输入框里时点击其他区域：先按失焦口径补位并归一化。</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || IsInside(source, HourBox)
            || IsInside(source, MinuteBox)
            || IsInside(source, SecondBox)
            || IsInside(source, CycleBox))
        {
            return;
        }

        if (HourBox.IsKeyboardFocusWithin
            || MinuteBox.IsKeyboardFocusWithin
            || SecondBox.IsKeyboardFocusWithin)
        {
            OnDurationCommit(this, new RoutedEventArgs());
        }
        else if (CycleBox.IsKeyboardFocusWithin)
        {
            OnCycleCommit(this, new RoutedEventArgs());
        }
    }

    private static bool IsInside(DependencyObject node, DependencyObject root)
    {
        var current = node;

        while (current is not null)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnCycleCommit(object sender, RoutedEventArgs e)
    {
        var cycles = TimerInputPolicy.ParseCycles(CycleBox.Text);
        CycleBox.Text = cycles.ToString();
        UpdateDisplay();
    }

    private void OnCycleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        OnCycleCommit(sender, e);
    }

    private bool TryReadTarget(out TimeSpan target)
    {
        NormalizeDurationBoxes();

        if (!TimerInputPolicy.TryParse(HourBox.Text, MinuteBox.Text, SecondBox.Text, out target))
        {
            HintText.Text = "目标时长需为非负整数，且总和大于 0。";
            return false;
        }

        ApplyTarget(target);
        CommitTargetForMode();
        return true;
    }

    /// <summary>把归一化后的时长回写到输入框，用户可直接看到 1 小时 15 分的拆分。</summary>
    private void ApplyTarget(TimeSpan target)
    {
        var (hours, minutes, seconds) = TimerInputPolicy.Split(target);
        HourBox.Text = hours.ToString("00", CultureInfo.InvariantCulture);
        MinuteBox.Text = minutes.ToString("00", CultureInfo.InvariantCulture);
        SecondBox.Text = seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 单个输入框失焦补位：空或非数字回到 00，10 以下补零，三位数（如 100 小时）原样保留。
    /// </summary>
    private void NormalizeDurationBoxes()
    {
        NormalizePart(HourBox);
        NormalizePart(MinuteBox);
        NormalizePart(SecondBox);
    }

    private static void NormalizePart(TextBox box)
    {
        var text = box.Text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            box.Text = "00";
            return;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            box.Text = "00";
            return;
        }

        box.Text = value < 100
            ? value.ToString("00", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>切换页签前把当前输入记到本页签的时长；非法或全零输入保留原值。</summary>
    private void CommitTargetForMode()
    {
        if (_mode == TimerMode.Stopwatch)
        {
            return;
        }

        if (!TimerInputPolicy.TryParse(HourBox.Text, MinuteBox.Text, SecondBox.Text, out var target))
        {
            return;
        }

        StoreTarget(target);
    }

    private void StoreTarget(TimeSpan target)
    {
        if (_mode == TimerMode.Pomodoro)
        {
            if (_pomodoroTarget == target)
            {
                return;
            }

            _pomodoroTarget = target;
            _settings.Set(PreferenceKeys.PomodoroTargetSeconds, ToSeconds(target), module: "clock");
            return;
        }

        if (_timerTarget == target)
        {
            return;
        }

        _timerTarget = target;
        _settings.Set(PreferenceKeys.TimerTargetSeconds, ToSeconds(target), module: "clock");
    }

    private static int ToSeconds(TimeSpan target) =>
        (int)Math.Clamp(target.TotalSeconds, 0, int.MaxValue);

    private void LoadTargetForMode() =>
        ApplyTarget(_mode == TimerMode.Pomodoro ? _pomodoroTarget : _timerTarget);

    private TimeSpan ReadStoredTarget(string key)
    {
        var seconds = _settings.Get(
            key,
            key == PreferenceKeys.PomodoroTargetSeconds
                ? PreferenceDefaults.PomodoroTargetSeconds
                : PreferenceDefaults.TimerTargetSeconds);

        return seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(PreferenceDefaults.TimerTargetSeconds);
    }

    /// <summary>待开始展示用目标时长；非法输入按 0 展示，不写入引擎。</summary>
    private TimeSpan CurrentTarget() =>
        TimerInputPolicy.TryParse(HourBox.Text, MinuteBox.Text, SecondBox.Text, out var target)
            ? target
            : TimeSpan.Zero;

    private int SelectedCycles()
    {
        var cycles = TimerInputPolicy.ParseCycles(CycleBox.Text);
        CycleBox.Text = cycles.ToString();
        return cycles;
    }

    private void OnZoomClick(object sender, RoutedEventArgs e)
    {
        OpenFullscreen();
    }

    private void OpenFullscreen()
    {
        if (_fullscreen is not null && _fullscreen.IsVisible)
        {
            _fullscreen.Activate();
            return;
        }

        _fullscreen = new TimerFullscreenWindow(_mode, _timer, _stopwatch, _pomodoro, _settings);
        _fullscreen.Closed += (_, _) => _fullscreen = null;
        _fullscreen.Show();
    }

    private void OnMiniClick(object sender, RoutedEventArgs e)
    {
        // 三种模式各自对应独立小组件实例，位置与显示状态互不影响。
        var kind = _mode switch
        {
            TimerMode.Stopwatch => WidgetKind.Stopwatch,
            TimerMode.Pomodoro => WidgetKind.Pomodoro,
            _ => WidgetKind.Timer,
        };

        var state = _widgetHost.GetState(kind);
        _widgetHost.ShowWidget(state);

        HintText.Text = _mode switch
        {
            TimerMode.Stopwatch => "已缩小为秒表小组件：可开始/暂停、计圈、重置与关闭。",
            TimerMode.Pomodoro => "已缩小为番茄钟小组件：可开始/暂停、重置与关闭。",
            _ => "已缩小为计时器小组件：可开始/暂停、标记、重置与关闭。",
        };

        MiniRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatTimer(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}";
    }

    private static string FormatStopwatch(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}.{total.Milliseconds / 10:00}";
    }
}
