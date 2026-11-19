using System.Windows;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// 剪贴板浮窗生命周期：全局热键、剪贴板页按钮共用同一实例；
/// 窗口关闭后按需重建，隐藏时保留已建窗口以减少重复布局。
/// </summary>
public sealed class ClipboardFloatController : IDisposable
{
    private readonly ClipboardBoardService _board;
    private readonly IClipboardMonitor _monitor;
    private readonly ISettingsStore _settings;
    private ClipboardFloatWindow? _window;
    private bool _disposed;

    public ClipboardFloatController(
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
    }

    /// <summary>快捷键切换：已激活则隐藏，否则显示并聚焦搜索。</summary>
    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is { IsVisible: true, IsActive: true })
        {
            _window.Hide();
            return;
        }

        Show();
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is null)
        {
            _window = new ClipboardFloatWindow(_board, _monitor, _settings);
            _window.Closed += (_, _) => _window = null;
        }

        if (!_window.IsVisible)
        {
            _window.ShowFloat();
        }
        else
        {
            _window.Activate();
        }
    }

    public void Hide() => _window?.Hide();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window?.Close();
        _window = null;
    }
}
