using System.Windows;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>便签桌面组件窗口：拖动、置顶、关闭，绑定指定便签；高度由内容自动决定。</summary>
public sealed class NoteWidgetWindow : WidgetWindowBase
{
    private const double MinimumHeight = 150;
    private const double MaximumHeight = 520;

    private readonly NoteWidgetView _view;

    public NoteWidgetWindow(
        INoteService notes,
        WidgetState state,
        Action<WidgetState> stateChanged,
        Action<WidgetKind> closed)
        : base(state, stateChanged, closed)
    {
        Title = "便签组件";
        _view = new NoteWidgetView(notes, state.SourceId);
        _view.DragRequested += OnDragRequested;
        _view.CloseRequested += OnCloseRequested;
        _view.Unloaded += OnViewUnloaded;

        // 高度按内容自适应：内容少时收紧、内容多时增高，超过上限后正文滚动。
        MinHeight = MinimumHeight;
        MaxHeight = MaximumHeight;

        _view.ContentChanged += OnViewContentChanged;
        Loaded += (_, _) => ApplyContentHeight();

        Content = _view;
    }

    protected override FrameworkElement InteractiveElement => _view.CardElement;

    public void ApplySource(long? sourceId) => _view.ApplySource(sourceId);

    private void OnViewContentChanged()
    {
        // 内容在布局完成后才可测量，延后到 Loaded 优先级统一处理。
        Dispatcher.BeginInvoke(
            new Action(ApplyContentHeight),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyContentHeight()
    {
        if (!IsLoaded || !_view.IsLoaded)
        {
            return;
        }

        var target = Math.Clamp(_view.PreferredHeight(), MinimumHeight, MaximumHeight);
        if (Math.Abs(Height - target) < 1)
        {
            return;
        }

        Height = target;
        SaveStateFromWindow();
    }

    private void OnDragRequested(object? sender, EventArgs e) => DragByCard();

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        _view.DragRequested -= OnDragRequested;
        _view.CloseRequested -= OnCloseRequested;
        _view.ContentChanged -= OnViewContentChanged;
        _view.Unloaded -= OnViewUnloaded;
    }
}
