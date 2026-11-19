using System.Windows;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>今日日程/待办小组件窗口：可拖动、置顶与关闭。</summary>
public sealed class ScheduleWidgetWindow : WidgetWindowBase
{
    private readonly ScheduleWidgetView _view;

    public ScheduleWidgetWindow(
        ICalendarService calendar,
        IScheduleService schedules,
        ITodoService todos,
        WidgetState state,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed)
        : base(state, stateChanged, closed)
    {
        Title = "今日日程小组件";
        _view = new ScheduleWidgetView(calendar, schedules, todos);
        _view.DragRequested += OnDragRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.Unloaded += OnViewUnloaded;
        Content = _view;
    }

    protected override FrameworkElement InteractiveElement => _view.CardElement;

    private void OnDragRequested(object? sender, EventArgs e) => DragByCard();

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnViewUnloaded(object? sender, RoutedEventArgs e)
    {
        _view.DragRequested -= OnDragRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.Unloaded -= OnViewUnloaded;
    }
}
