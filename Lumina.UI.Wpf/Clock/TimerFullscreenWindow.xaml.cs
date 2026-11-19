using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// P1-03 全屏专注窗口：展示计时器/秒表/番茄的实时进度，连按两下 ESC 才退出。
/// 单击 ESC 不退出，双击时间窗为 400 ms。
/// </summary>
public partial class TimerFullscreenWindow : Window
{
    private static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(400);

    private readonly TimerMode _mode;
    private readonly ITimerEngine _timer;
    private readonly IStopwatchEngine _stopwatch;
    private readonly IPomodoroService _pomodoro;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _refresh;

    private DateTimeOffset _lastEscapeAt = DateTimeOffset.MinValue;

    public TimerFullscreenWindow(
        TimerMode mode,
        ITimerEngine timer,
        IStopwatchEngine stopwatch,
        IPomodoroService pomodoro,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stopwatch);
        ArgumentNullException.ThrowIfNull(pomodoro);
        ArgumentNullException.ThrowIfNull(settings);

        _mode = mode;
        _timer = timer;
        _stopwatch = stopwatch;
        _pomodoro = pomodoro;
        _settings = settings;

        InitializeComponent();

        MarkButtonVisible();
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refresh.Tick += OnRefresh;
        Loaded += (_, _) => _refresh.Start();
        Closed += (_, _) => _refresh.Stop();
        UpdateDisplay();
    }

    private void MarkButtonVisible()
    {
        // 只有秒表需要计圈标记。
        MarkButton.Visibility = _mode == TimerMode.Stopwatch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRefresh(object? sender, EventArgs e)
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        switch (_mode)
        {
            case TimerMode.Stopwatch:
                {
                    var state = _stopwatch.State;
                    LabelText.Text = "秒 表";
                    TimeText.Text = FormatStopwatch(state.Elapsed);
                    PlayPauseButton.Content = state.IsRunning ? "暂停" : state.IsPaused ? "继续" : "开始";
                    break;
                }

            case TimerMode.Pomodoro:
                {
                    var state = _pomodoro.Current;
                    LabelText.Text = state.Phase switch
                    {
                        PomodoroPhase.Focus => $"专 注 计 时 · 第 {state.Cycle} / {state.TotalCycles} 轮",
                        PomodoroPhase.Break => $"休 息 · 第 {state.Cycle} / {state.TotalCycles} 轮",
                        PomodoroPhase.Paused => "已 暂 停",
                        _ when state.Completed => $"全 部 完 成 · 共 {state.TotalCycles} 轮",
                        _ => "专 注 计 时",
                    };
                    TimeText.Text = FormatTimer(state.Remaining);
                    PlayPauseButton.Content = state.Phase switch
                    {
                        PomodoroPhase.Focus or PomodoroPhase.Break => "暂停",
                        PomodoroPhase.Paused => "继续",
                        _ => "开始",
                    };
                    break;
                }

            default:
                {
                    var state = _timer.State;
                    LabelText.Text = "专 注 计 时";
                    TimeText.Text = FormatTimer(state.Remaining);
                    PlayPauseButton.Content = state.IsRunning ? "暂停" : state.IsPaused ? "继续" : "开始";
                    break;
                }
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case TimerMode.Stopwatch:
                if (_stopwatch.State.IsRunning)
                {
                    _stopwatch.Pause();
                }
                else
                {
                    _stopwatch.Start();
                }

                break;

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
                        var seconds = _settings.Get(
                            PreferenceKeys.PomodoroTargetSeconds,
                            PreferenceDefaults.PomodoroTargetSeconds);

                        var focus = TimeSpan.FromSeconds(
                            seconds > 0 ? seconds : PreferenceDefaults.PomodoroTargetSeconds);

                        // 全部完成后再开始沿用上一次的轮数；重置后恢复默认轮数。
                        var cycles = state.Completed
                            ? Math.Max(1, state.TotalCycles)
                            : TimerInputPolicy.DefaultCycles;

                        _pomodoro.Start(focus, PomodoroTimingPolicy.RestFor(focus), cycles);
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

                    break;
                }
        }

        UpdateDisplay();
    }

    private void OnMarkClick(object sender, RoutedEventArgs e)
    {
        _stopwatch.MarkLap();
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

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (now - _lastEscapeAt <= DoublePressWindow)
        {
            Close();
            return;
        }

        _lastEscapeAt = now;
        EscHintText.Text = "再按一次 ESC 退出全屏";
        e.Handled = true;
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
