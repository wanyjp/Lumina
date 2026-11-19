using System.Windows;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>
/// 计时类小组件窗口：计时器 / 秒表 / 番茄钟各自独立实例，
/// 卡片内含开始暂停、标记（计圈）、重置与关闭，支持“小 / 大”两种密度。
/// </summary>
public sealed class TimerWidgetWindow : WidgetWindowBase
{
    private readonly TimerWidgetView _view;
    private readonly TimerWidgetMode _mode;
    private readonly Action? _returnToMain;

    public TimerWidgetWindow(
        ITimerEngine timer,
        IStopwatchEngine stopwatch,
        IPomodoroService pomodoro,
        ISettingsStore settings,
        TimerWidgetMode mode,
        WidgetState state,
        bool compact,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed,
        Action? returnToMain = null)
        : base(state, stateChanged, closed)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _mode = mode;
        _returnToMain = returnToMain;
        Title = mode switch
        {
            TimerWidgetMode.Stopwatch => "秒表小组件",
            TimerWidgetMode.Pomodoro => "番茄钟小组件",
            _ => "计时器小组件",
        };

        _view = new TimerWidgetView(timer, stopwatch, pomodoro, settings, mode, compact);
        _view.DragRequested += OnDragRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.ReturnRequested += OnReturnRequested;
        _view.Unloaded += OnViewUnloaded;
        Content = _view;

        var size = PreferredSize(mode, compact);
        Width = size.Width;
        Height = size.Height;
    }

    /// <summary>「小」为贴近截图的方形卡片；「大」保留状态与提示行。</summary>
    public static (double Width, double Height) PreferredSize(TimerWidgetMode mode, bool compact) =>
        compact
            ? (184, 150)
            : mode == TimerWidgetMode.Stopwatch
                ? (360, 196)
                : (340, 196);

    protected override FrameworkElement InteractiveElement => _view.CardElement;

    /// <summary>按设置切换“小 / 大”密度并同步窗口尺寸。</summary>
    public void ApplyCompact(bool compact)
    {
        _view.ApplyCompact(compact);
        var size = PreferredSize(_mode, compact);
        Width = size.Width;
        Height = size.Height;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                UpdateLayout();
                SaveStateFromWindow();
            }),
            DispatcherPriority.Loaded);
    }

    private void OnDragRequested(object? sender, EventArgs e)
    {
        DragByCard();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnReturnRequested(object? sender, EventArgs e)
    {
        Close();
        _returnToMain?.Invoke();
    }

    private void OnViewUnloaded(object? sender, RoutedEventArgs e)
    {
        _view.DragRequested -= OnDragRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.ReturnRequested -= OnReturnRequested;
        _view.Unloaded -= OnViewUnloaded;
    }
}
