using System.Windows;
using System.Windows.Input;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// 剪贴板浮窗：置顶无边框容器，内嵌完整 <see cref="ClipboardView"/>，
/// 因此分类、搜索、标签、详情、滑动编辑/删除、多选与回贴能力与主页一致；
/// 双击回填后默认隐藏，右上角「固定」可保持常驻。
/// </summary>
public partial class ClipboardFloatWindow : Window
{
    private readonly ClipboardBoardService _board;
    private readonly IClipboardMonitor _monitor;
    private readonly ISettingsStore _settings;

    private ClipboardView? _view;
    private bool _stayOpen;

    public ClipboardFloatWindow(
        ClipboardBoardService board,
        IClipboardMonitor monitor,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);

        _board = board;
        _monitor = monitor;
        _settings = settings;

        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    /// <summary>显示浮窗；首次打开时创建完整剪贴板页并去掉重复外框与标题。</summary>
    public void ShowFloat()
    {
        EnsureView();
        _view?.Refresh();

        if (!IsVisible)
        {
            Show();
        }

        PositionNearRight();
        Activate();
        _view?.Focus();
    }

    private void EnsureView()
    {
        if (_view is not null)
        {
            return;
        }

        _view = new ClipboardView(_board, _monitor, floatController: null, _settings);
        _view.ApplyFloatMode();
        _view.PasteBackCompleted += (_, _) =>
        {
            if (!_stayOpen)
            {
                Hide();
            }
        };
        ViewHost.Content = _view;
    }

    private void PositionNearRight()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        Left = Math.Max(workArea.Left, workArea.Right - width - 24);
        Top = Math.Max(workArea.Top, workArea.Top + (workArea.Height - height) / 2);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _stayOpen = !_stayOpen;
        PinButton.Content = _stayOpen ? "取消固定" : "固定";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Hide();
        }
    }
}
