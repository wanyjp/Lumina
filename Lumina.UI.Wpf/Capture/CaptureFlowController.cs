using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Lumina.Services.Capture;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// 截图流程编排：隐藏主窗口 → 调用捕获服务 → 打开标注编辑器 →
/// 落盘 / 复制 / 贴图 → 恢复主窗口。四类截图共用同一管线；
/// 贴图窗口由本控制器集中登记与回收。
/// </summary>
public sealed class CaptureFlowController
{
    private readonly IScreenCaptureService _capture;
    private readonly IScreenshotStore _store;
    private readonly IClipboardWriter _clipboard;
    private readonly List<PinnedImageWindow> _pins = [];
    private List<PinnedImageWindow>? _suspendedPins;

    private bool _running;

    public CaptureFlowController(
        IScreenCaptureService capture,
        IScreenshotStore store,
        IClipboardWriter clipboard)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clipboard);

        _capture = capture;
        _store = store;
        _clipboard = clipboard;
    }

    /// <summary>
    /// 窗口截图的选窗回调；由应用层注入（选窗 UI 依赖 SystemAdapters 的窗口枚举，
    /// 而 UI.Wpf 不引用 SystemAdapters）。返回null 表示用户取消。
    /// </summary>
    public Func<Window?, nint?>? WindowPicker { get; set; }

    public int PinCount => _pins.Count;

    /// <summary>区域截图：主窗口先隐藏，遮罩框选后进入标注编辑器。</summary>
    public Task<string> RunRegionAsync() => RunAsync(hideMainWindow: true, CaptureRegionCoreAsync);

    /// <summary>全屏截图：虚拟屏整屏捕获。</summary>
    public Task<string> RunFullScreenAsync() => RunAsync(hideMainWindow: true, CaptureFullScreenCoreAsync);

    /// <summary>延时截图：3 秒倒计时后全屏捕获。</summary>
    public Task<string> RunDelayedAsync() => RunAsync(hideMainWindow: true, CaptureDelayedCoreAsync);

    /// <summary>窗口截图：先弹出选窗器，捕获所选窗口（WGC 优先）。</summary>
    public Task<string> RunWindowAsync() => RunAsync(hideMainWindow: false, CaptureWindowCoreAsync);

    /// <summary>关闭全部贴图窗口（应用退出时调用）。</summary>
    public void CloseAllPins()
    {
        foreach (var pin in _pins.ToList())
        {
            pin.Close();
        }
    }

    private async Task<string> RunAsync(bool hideMainWindow, Func<Task<string>> flow)
    {
        if (_running)
        {
            return "已有截图任务进行中，请先完成当前操作。";
        }

        _running = true;
        var mainWindow = Application.Current?.MainWindow;
        var wasVisible = mainWindow is { IsVisible: true };

        try
        {
            if (hideMainWindow && wasVisible && mainWindow is not null)
            {
                mainWindow.Hide();
            }

            return await flow().ConfigureAwait(true);
        }
        finally
        {
            if (hideMainWindow && wasVisible && mainWindow is not null && !mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            _running = false;
        }
    }

    private async Task<string> CaptureRegionCoreAsync()
    {
        try
        {
            var result = await _capture.CaptureRegionAsync().ConfigureAwait(true);
            return Edit(result, "region");
        }
        catch (OperationCanceledException)
        {
            return "已取消区域截图。";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            return $"区域截图失败：{ex.Message}";
        }
    }

    private async Task<string> CaptureFullScreenCoreAsync()
    {
        try
        {
            var result = await _capture.CaptureFullScreenAsync().ConfigureAwait(true);
            return Edit(result, "full");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            return $"全屏截图失败：{ex.Message}";
        }
    }

    private async Task<string> CaptureDelayedCoreAsync()
    {
        try
        {
            var result = await _capture.CaptureDelayedAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            return Edit(result, "delay");
        }
        catch (OperationCanceledException)
        {
            return "已取消延时截图。";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            return $"延时截图失败：{ex.Message}";
        }
    }

    private async Task<string> CaptureWindowCoreAsync()
    {
        var picker = WindowPicker;
        if (picker is null)
        {
            return "窗口截图暂不可用：选窗组件未接入。";
        }

        var handle = picker(Application.Current?.MainWindow);
        if (handle is 0 or null)
        {
            return "已取消窗口截图。";
        }

        try
        {
            var result = await _capture.CaptureWindowAsync(handle.Value).ConfigureAwait(true);
            return Edit(result, "window");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            return $"窗口截图失败：{ex.Message}";
        }
    }

    private string Edit(CaptureResult capture, string captureType)
    {
        BitmapSource source;
        using (var stream = new MemoryStream(capture.ImageBytes))
        {
            source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        var editor = new AnnotationEditorWindow(source, _store, _clipboard, captureType);
        editor.ShowDialog();
        var result = editor.Result;

        if (result.Pinned && result.PinnedPng is not null)
        {
            OpenPin(result.PinnedPng);
        }

        if (result.Saved && result.SavedPath is not null)
        {
            return $"已保存：{Path.GetFileName(result.SavedPath)}";
        }

        if (result.Copied)
        {
            return "已复制到剪贴板。";
        }

        if (result.Pinned)
        {
            return "已贴图到桌面（可拖动 / 滚轮缩放 / Esc 关闭）。";
        }

        return "已放弃本次截图。";
    }

    private void OpenPin(byte[] png)
    {
        var pin = new PinnedImageWindow(png);
        pin.Closed += (_, _) => _pins.Remove(pin);
        _pins.Add(pin);
        pin.Show();
        pin.Activate();
    }

    /// <summary>
    /// 取色前临时隐藏贴图，避免置顶贴图遮挡目标像素；只隐藏原本可见的窗口，
    /// 结束后由 <see cref="ResumePins"/> 原样恢复，不改变用户已固定的贴图集合。
    /// </summary>
    public void SuspendPins()
    {
        if (_suspendedPins is not null)
        {
            return;
        }

        _suspendedPins = _pins.Where(pin => pin.IsVisible).ToList();
        foreach (var pin in _suspendedPins)
        {
            pin.Hide();
        }
    }

    public void ResumePins()
    {
        var suspended = _suspendedPins;
        _suspendedPins = null;
        if (suspended is null)
        {
            return;
        }

        foreach (var pin in suspended)
        {
            pin.Show();
        }
    }
}
