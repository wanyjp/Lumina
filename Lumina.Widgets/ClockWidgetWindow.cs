using System.Windows;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>时钟小组件窗口：承载 <see cref="ClockWidgetView"/> 并接管拖动与关闭。</summary>
public sealed class ClockWidgetWindow : WidgetWindowBase
{
    private readonly ClockWidgetView _view;

    public ClockWidgetWindow(
        IClockService clock,
        IWorldClockService worldClock,
        ISettingsStore settings,
        WidgetState state,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed)
        : base(state, stateChanged, closed)
    {
        Title = "时钟小组件";
        _view = new ClockWidgetView(clock, worldClock, settings);
        _view.DragRequested += OnDragRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.Unloaded += OnViewUnloaded;
        Content = _view;
    }

    protected override FrameworkElement InteractiveElement => _view.CardElement;

    private void OnDragRequested(object? sender, EventArgs e)
    {
        DragByCard();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnViewUnloaded(object? sender, RoutedEventArgs e)
    {
        _view.DragRequested -= OnDragRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.Unloaded -= OnViewUnloaded;
    }
}
