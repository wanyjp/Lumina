using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>计时类小组件的模式；三种模式各自是独立实例与独立位置。</summary>
public enum TimerWidgetMode
{
    Timer = 0,
    Stopwatch = 1,
    Pomodoro = 2,
}

/// <summary>
/// 计时类小组件内容：计时器 / 秒表 / 番茄钟共用一套卡片，按模式切换展示与按钮，
/// 提供开始/暂停、标记（计时器为时间标记、秒表为计圈）、重置与关闭。
/// 支持“小 / 大”两种密度，由设置项决定。
/// </summary>
public partial class TimerWidgetView : UserControl, IWidgetGlassView
{
    private readonly ITimerEngine _timer;
    private readonly IStopwatchEngine _stopwatch;
    private readonly IPomodoroService _pomodoro;
    private readonly ISettingsStore _settings;
    private readonly TimerWidgetMode _mode;
    private readonly DispatcherTimer _refresh;
    private readonly List<TextBlock> _compactDigits = [];

    private bool _compact;
    private int _compactGroupCount;

    private string ModeName => _mode switch
    {
        TimerWidgetMode.Stopwatch => "秒表",
        TimerWidgetMode.Pomodoro => "番茄钟",
        _ => "计时器",
    };

    public TimerWidgetView(
        ITimerEngine timer,
        IStopwatchEngine stopwatch,
        IPomodoroService pomodoro,
        ISettingsStore settings,
        TimerWidgetMode mode,
        bool compact)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stopwatch);
        ArgumentNullException.ThrowIfNull(pomodoro);
        ArgumentNullException.ThrowIfNull(settings);

        _timer = timer;
        _stopwatch = stopwatch;
        _pomodoro = pomodoro;
        _settings = settings;
        _mode = mode;
        _compact = compact;

        InitializeComponent();
        ApplyMode();
        ApplyDensity();

        // 卡片级拖动必须排除按钮：否则按下按钮会先进入 DragMove，点击被吞掉。
        Card.PreviewMouseLeftButtonDown += OnCardMouseDown;

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refresh.Tick += OnRefresh;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateDisplay();
    }

    public event EventHandler? DragRequested;

    public event EventHandler? CloseRequested;

    /// <summary>返回主窗口：由宿主关闭小组件并唤起主窗口。</summary>
    public event EventHandler? ReturnRequested;

    public Border CardElement => Card;

    public WidgetGlassSurface GlassSurface => Glass;

    /// <summary>切换“小 / 大”密度：小档隐藏状态与提示行、缩小字号。</summary>
    public void ApplyCompact(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }

        _compact = compact;
        ApplyDensity();
    }

    private void ApplyMode()
    {
        // 番茄钟只保留开始/暂停、重置与关闭；计时器的标记与秒表的计圈各自保留。
        MarkButton.ToolTip = _mode switch
        {
            TimerWidgetMode.Stopwatch => "计圈",
            _ => "标记",
        };
        MarkButton.Visibility = _mode == TimerWidgetMode.Pomodoro
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyDensity()
    {
        var compactFont = _mode == TimerWidgetMode.Stopwatch ? 18 : 22;
        TimeText.FontSize = _compact ? compactFont : 38;
        TimeText.LineHeight = _compact ? compactFont + 4 : 44;
        TimeText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        CompactTimeGrid.Visibility = _compact ? Visibility.Visible : Visibility.Collapsed;
        LabelText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        ModeText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        HintText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        ReturnButton.Visibility = _compact ? Visibility.Visible : Visibility.Collapsed;
        RootGrid.Margin = _compact ? new Thickness(4) : new Thickness(10);
        Card.Padding = _compact ? new Thickness(8, 6, 8, 8) : new Thickness(14, 10, 14, 10);
        ButtonRow.Margin = new Thickness(0, _compact ? 4 : 8, 0, 0);

        // 小尺寸压缩按钮与间距，给数字区让出宽度。
        var buttonSize = _compact ? 30 : 34;
        foreach (var button in new[] { PlayPauseButton, MarkButton, ResetButton })
        {
            button.Width = buttonSize;
            button.Height = buttonSize;
            button.Margin = new Thickness(
                ReferenceEquals(button, PlayPauseButton) ? 0 : (_compact ? 8 : 10),
                0,
                0,
                0);
        }

        // 字号变化后需要按新字号重建数字行。
        _compactGroupCount = 0;
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

    private void OnRefresh(object? sender, EventArgs e) => UpdateDisplay();

    private void UpdateDisplay()
    {
        switch (_mode)
        {
            case TimerWidgetMode.Stopwatch:
                UpdateStopwatch();
                break;
            case TimerWidgetMode.Pomodoro:
                UpdatePomodoro();
                break;
            default:
                UpdateTimer();
                break;
        }

        // 小尺寸下 LabelText / HintText 被隐藏；计时器与秒表的标记次数仍要可见。
        var markCount = MarkCount;
        ModeText.Text = markCount == 0 ? ModeName : $"{ModeName} · {markCount} 次标记";

        if (_compact)
        {
            RenderCompactTime(TimeText.Text);
        }
    }

    /// <summary>
    /// 小尺寸时间行：数字与单位放在同一个 Grid 的两行里共用列宽，
    /// 「小时 / 分钟 / 秒」因此逐组落在对应数字正下方，不再整行居中导致错位。
    /// </summary>
    private void RenderCompactTime(string text)
    {
        var parts = text.Split(':');
        var groups = new List<string>();
        var tokens = new List<(string Text, bool IsSeparator)>();

        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
            {
                tokens.Add((":", true));
            }

            var part = parts[index];
            if (index == parts.Length - 1 && part.Contains('.'))
            {
                var dot = part.IndexOf('.');
                var seconds = part[..dot];
                tokens.Add((seconds, false));
                groups.Add(seconds);
                tokens.Add((".", true));
                var fraction = part[(dot + 1)..];
                tokens.Add((fraction, false));
                groups.Add(fraction);
            }
            else
            {
                tokens.Add((part, false));
                groups.Add(part);
            }
        }

        if (groups.Count != _compactGroupCount)
        {
            BuildCompactTime(tokens, groups.Count);
            _compactGroupCount = groups.Count;
        }

        for (var index = 0; index < _compactDigits.Count && index < groups.Count; index++)
        {
            _compactDigits[index].Text = groups[index];
        }
    }

    private void BuildCompactTime(List<(string Text, bool IsSeparator)> tokens, int groupCount)
    {
        CompactTimeGrid.Children.Clear();
        CompactTimeGrid.ColumnDefinitions.Clear();
        _compactDigits.Clear();

        string[] labels = groupCount switch
        {
            4 => ["小时", "分钟", "秒", string.Empty],
            3 => ["小时", "分钟", "秒"],
            2 => ["分钟", "秒"],
            _ => [],
        };

        var digitStyle = (Style)FindResource("Theme.Text.Mono");
        var labelStyle = (Style)FindResource("Theme.Text.Tertiary");
        var groupIndex = 0;

        for (var index = 0; index < tokens.Count; index++)
        {
            var (text, isSeparator) = tokens[index];
            CompactTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (isSeparator)
            {
                var separator = new TextBlock
                {
                    Text = text,
                    Style = digitStyle,
                    FontSize = TimeText.FontSize,
                    FontWeight = TimeText.FontWeight,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                Grid.SetRow(separator, 0);
                Grid.SetColumn(separator, index);
                CompactTimeGrid.Children.Add(separator);
                continue;
            }

            var digit = new TextBlock
            {
                Text = text,
                Style = digitStyle,
                FontSize = TimeText.FontSize,
                FontWeight = TimeText.FontWeight,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            Grid.SetRow(digit, 0);
            Grid.SetColumn(digit, index);
            CompactTimeGrid.Children.Add(digit);
            _compactDigits.Add(digit);

            var labelText = groupIndex < labels.Length ? labels[groupIndex] : string.Empty;
            if (labelText.Length > 0)
            {
                var label = new TextBlock
                {
                    Text = labelText,
                    Style = labelStyle,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0),
                };
                label.SetResourceReference(TextBlock.FontSizeProperty, "Theme.FontSize.Caption");

                Grid.SetRow(label, 1);
                Grid.SetColumn(label, index);
                CompactTimeGrid.Children.Add(label);
            }

            groupIndex++;
        }
    }

    private void UpdateTimer()
    {
        var state = _timer.State;
        LabelText.Text = state.IsRunning ? "计时中" : state.IsPaused ? "已暂停" : "待开始";
        TimeText.Text = Format(state.Remaining);
        SetPlayState(state.IsRunning);
        var marks = _timer.Marks;
        HintText.Text = marks.Count == 0
            ? "标记记录已过时间点；重置清空标记。"
            : $"已标记 {marks.Count} 次 · 最近 {Format(marks[^1])}";
    }

    private void UpdateStopwatch()
    {
        var state = _stopwatch.State;
        LabelText.Text = state.IsRunning ? "计时中" : state.IsPaused ? "已暂停" : "待开始";
        TimeText.Text = FormatCentiseconds(state.Elapsed);
        SetPlayState(state.IsRunning);
        var laps = _stopwatch.Laps;
        HintText.Text = laps.Count == 0
            ? "计圈记录分段时间；重置清空计圈。"
            : $"已计 {laps.Count} 圈 · 最近 {FormatCentiseconds(laps[^1])}";
    }

    private void UpdatePomodoro()
    {
        var state = _pomodoro.Current;
        LabelText.Text = state.Phase switch
        {
            PomodoroPhase.Focus => $"专注 · 第 {state.Cycle}/{state.TotalCycles} 轮",
            PomodoroPhase.Break => $"休息 · 第 {state.Cycle}/{state.TotalCycles} 轮",
            PomodoroPhase.Paused => "已暂停",
            _ when state.Completed => "全部完成",
            _ => "待开始",
        };

        TimeText.Text = Format(state.Remaining);
        SetPlayState(state.Phase is PomodoroPhase.Focus or PomodoroPhase.Break);
        HintText.Text = "每轮专注结束记录一次会话；重置停止当前轮次。";
    }

    /// <summary>当前模式的标记数量：计时器读引擎标记，秒表读计圈，番茄钟无标记。</summary>
    private int MarkCount => _mode switch
    {
        TimerWidgetMode.Stopwatch => _stopwatch.Laps.Count,
        TimerWidgetMode.Timer => _timer.Marks.Count,
        _ => 0,
    };

    private void SetPlayState(bool running)
    {
        PlayPauseButton.Content = running ? "\uE769" : "\uE768";
        PlayPauseButton.ToolTip = running ? "暂停" : "开始";
    }

    private static string Format(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return total.TotalHours >= 1
            ? $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}"
            : $"{total.Minutes:00}:{total.Seconds:00}";
    }

    private static string FormatCentiseconds(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return $"{(int)total.TotalHours:00}:{total.Minutes:00}:{total.Seconds:00}.{total.Milliseconds / 10:00}";
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case TimerWidgetMode.Stopwatch:
                if (_stopwatch.State.IsRunning)
                {
                    _stopwatch.Pause();
                }
                else
                {
                    _stopwatch.Start();
                }

                break;

            case TimerWidgetMode.Pomodoro:
                var pomodoro = _pomodoro.Current;
                if (pomodoro.Phase == PomodoroPhase.Paused)
                {
                    _pomodoro.Resume();
                }
                else if (pomodoro.Phase is PomodoroPhase.Focus or PomodoroPhase.Break)
                {
                    _pomodoro.Pause();
                }
                else
                {
                    var seconds = _settings.Get(
                        PreferenceKeys.PomodoroTargetSeconds,
                        PreferenceDefaults.PomodoroTargetSeconds);

                    var focus = TimeSpan.FromSeconds(
                        seconds > 0 ? seconds : PreferenceDefaults.PomodoroTargetSeconds);

                    // 与全屏窗口一致：完成态沿用上次轮数，重置后回到默认 4 轮。
                    var cycles = pomodoro.Completed
                        ? Math.Max(1, pomodoro.TotalCycles)
                        : TimerInputPolicy.DefaultCycles;

                    _pomodoro.Start(focus, PomodoroTimingPolicy.RestFor(focus), cycles);
                }

                break;

            default:
                var timer = _timer.State;
                if (timer.IsRunning)
                {
                    _timer.Pause();
                }
                else if (timer.IsPaused)
                {
                    _timer.Resume();
                }
                else
                {
                    var seconds = _settings.Get(
                        PreferenceKeys.TimerTargetSeconds,
                        PreferenceDefaults.TimerTargetSeconds);

                    _timer.Start(TimeSpan.FromSeconds(
                        seconds > 0 ? seconds : PreferenceDefaults.TimerTargetSeconds));
                }

                break;
        }

        UpdateDisplay();
    }

    /// <summary>
    /// 标记写入引擎（与主界面共用同一份数据），小组件不再维护本地列表，
    /// 因此在小组件上标记后主界面立即可见，重置也会同时清空两处。
    /// </summary>
    private void OnMarkClick(object sender, RoutedEventArgs e)
    {
        if (_mode == TimerWidgetMode.Stopwatch)
        {
            var stopwatch = _stopwatch.State;
            if (stopwatch.IsRunning || stopwatch.IsPaused)
            {
                _stopwatch.MarkLap();
            }
        }
        else if (_mode == TimerWidgetMode.Timer)
        {
            var timer = _timer.State;
            if (timer.IsRunning || timer.IsPaused)
            {
                _timer.Mark();
            }
        }

        UpdateDisplay();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case TimerWidgetMode.Stopwatch:
                _stopwatch.Reset();
                break;
            case TimerWidgetMode.Pomodoro:
                _pomodoro.ForceStop();
                break;
            default:
                _timer.Reset();
                break;
        }

        UpdateDisplay();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnReturnClick(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke(this, EventArgs.Empty);

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        DragRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>事件是否落在按钮上；这些控件自行处理点击，不触发整卡拖动。</summary>
    private static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
