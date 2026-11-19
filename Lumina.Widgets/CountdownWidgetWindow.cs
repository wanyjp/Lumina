using System.Windows;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>倒计时桌面组件窗口：拖动、置顶、关闭，绑定指定倒计时。</summary>
public sealed class CountdownWidgetWindow : WidgetWindowBase
{
    private readonly CountdownWidgetView _view;

    public CountdownWidgetWindow(
        ICountdownService countdowns,
        WidgetState state,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed)
        : base(state, stateChanged, closed)
    {
        Title = "倒计时组件";
        _view = new CountdownWidgetView(countdowns, state.SourceId);
        _view.DragRequested += OnDragRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.Unloaded += OnViewUnloaded;
        Content = _view;
    }

    protected override FrameworkElement InteractiveElement => _view.CardElement;

    public void ApplySource(long? sourceId) => _view.ApplySource(sourceId);

    private void OnDragRequested(object? sender, EventArgs e) => DragByCard();

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        _view.DragRequested -= OnDragRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.Unloaded -= OnViewUnloaded;
    }
}
