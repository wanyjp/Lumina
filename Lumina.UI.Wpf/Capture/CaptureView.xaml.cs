using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Capture;
using Lumina.Services.Colors;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Settings;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// P2-04 截图 + P2-05 取色页：四类截图入口、全屏放大镜取色、历史色板与结果反馈。
/// OCR（P2-06）、截图历史与上限（P2-07）仍以阶段说明占位。
/// </summary>
public partial class CaptureView : UserControl
{
    private readonly CaptureFlowController _flow;
    private readonly IColorProbe _colorProbe;
    private readonly ColorHistoryService _colorHistory;
    private readonly IClipboardWriter _clipboard;
    private readonly IOcrService _ocr;
    private readonly IScreenCaptureService _capture;
    private readonly IScreenshotStore _screenshotStore;
    private readonly ISettingsStore _settings;
    private readonly HotkeyBindingService _hotkeys;

    private ColorSample? _currentColor;
    private CancellationTokenSource? _toastCts;
    private List<ScreenshotHistoryItem> _historyItems = [];
    private bool _selectionMode;
    private bool _busy;
    private IDisposable? _settingsSubscription;

    public CaptureView(
        CaptureFlowController flow,
        IColorProbe colorProbe,
        ColorHistoryService colorHistory,
        IClipboardWriter clipboard,
        IOcrService ocr,
        IScreenCaptureService capture,
        IScreenshotStore screenshotStore,
        ISettingsStore settings,
        HotkeyBindingService hotkeys)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(colorProbe);
        ArgumentNullException.ThrowIfNull(colorHistory);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(screenshotStore);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeys);

        _flow = flow;
        _colorProbe = colorProbe;
        _colorHistory = colorHistory;
        _clipboard = clipboard;
        _ocr = ocr;
        _capture = capture;
        _screenshotStore = screenshotStore;
        _settings = settings;
        _hotkeys = hotkeys;

        InitializeComponent();

        StorageText.Text = AppPaths.ScreenshotsDirectory;
        StorageText.ToolTip = AppPaths.ScreenshotsDirectory;
        HotkeyHintText.Text = BuildHotkeyHint();

        if (!_ocr.Available)
        {
            OcrButton.IsEnabled = false;
            OcrStatusText.Text = "当前系统缺少 OCR 语言包，OCR 不可用";
            OcrHintText.Text =
                "当前系统缺少 OCR 语言包，OCR 不可用。可在 Windows 设置 → 时间和语言 → 语言中添加语言包后重启拾光。";
        }

        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        ThumbOnlyCheck.IsChecked = _settings.Get(
            PreferenceKeys.ScreenshotThumbOnly,
            PreferenceDefaults.ScreenshotThumbOnly);
        Loaded += (_, _) =>
        {
            _settingsSubscription ??= _settings.Changed.Subscribe(
                new ActionObserver<SettingChanged>(OnSettingChanged));
            RenderHistory();
            RefreshScreenshotHistory();
        };
        Unloaded += (_, _) =>
        {
            _toastCts?.Cancel();
            _settingsSubscription?.Dispose();
            _settingsSubscription = null;
        };
    }

    /// <summary>设置抽屉修改保留张数或缩略图策略后，历史列表立即按新口径刷新。</summary>
    private void OnSettingChanged(SettingChanged change)
    {
        if (change.Key == PreferenceKeys.ScreenshotRetentionLimit)
        {
            _screenshotStore.EnforceLimit();
            RefreshScreenshotHistory();
            return;
        }

        if (change.Key == PreferenceKeys.ScreenshotThumbOnly)
        {
            ThumbOnlyCheck.IsChecked = _settings.Get(
                PreferenceKeys.ScreenshotThumbOnly,
                PreferenceDefaults.ScreenshotThumbOnly);
        }
    }

    /// <summary>截图历史卡片的数据项；<see cref="IsSelected"/> 供多选删除使用。</summary>
    private sealed class ScreenshotHistoryItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _showCheckBox;

        public ScreenshotHistoryItem(
            long id,
            ImageSource? thumbnail,
            string title,
            string subtitle,
            string fullPath,
            bool hasOriginal)
        {
            Id = id;
            Thumbnail = thumbnail;
            Title = title;
            Subtitle = subtitle;
            FullPath = fullPath;
            HasOriginal = hasOriginal;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public long Id { get; }

        public ImageSource? Thumbnail { get; }

        public string Title { get; }

        public string Subtitle { get; }

        public string FullPath { get; }

        public bool HasOriginal { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        /// <summary>是否处于多选删除模式；控制卡片左上角复选框的显隐。</summary>
        public bool ShowCheckBox
        {
            get => _showCheckBox;
            set
            {
                if (_showCheckBox == value)
                {
                    return;
                }

                _showCheckBox = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCheckBox)));
            }
        }
    }

    /// <summary>切换到截图模块时由主壳调用，确保模块内快捷键获得焦点。</summary>
    public void FocusForShortcuts() => Focus();

    /// <summary>按当前绑定拼出模块内快捷键说明，改键后下一次进入模块即更新。</summary>
    private string BuildHotkeyHint()
    {
        var pick = _hotkeys.Get(HotkeyActions.ColorPick);
        var copy = _hotkeys.Get(HotkeyActions.CopyColorValue);
        var pickText = HotkeyCatalog.IsUnbound(pick) ? "未设置" : pick.ToString();
        var copyText = HotkeyCatalog.IsUnbound(copy) ? "未设置" : copy.ToString();

        return $"快捷键：{pickText} 取色 · {copyText} 复制当前色值"
            + "（截图模块聚焦时生效，可在设置中心改键）";
    }

    private async void OnRegionClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _flow.RunRegionAsync());

    private async void OnFullScreenClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _flow.RunFullScreenAsync());

    private async void OnWindowClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _flow.RunWindowAsync());

    private async void OnDelayedClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => _flow.RunDelayedAsync());

    private async void OnPickColorClick(object sender, RoutedEventArgs e) => await PickColorAsync();

    private async void OnCopyColorClick(object sender, RoutedEventArgs e) =>
        await CopyColorAsync(_currentColor?.Hex);

    private async void OnOcrClick(object sender, RoutedEventArgs e) => await RunOcrAsync();

    private async void OnCopyOcrClick(object sender, RoutedEventArgs e) => await CopyOcrAsync();

    /// <summary>
    /// 模块内快捷键（取色 / 复制色值）：只在截图模块获得焦点时生效，
    /// 组合来自统一快捷键表，用户改键后立即按新组合匹配。
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (MatchesGesture(HotkeyActions.ColorPick, key))
        {
            e.Handled = true;
            await PickColorAsync();
        }
        else if (MatchesGesture(HotkeyActions.CopyColorValue, key))
        {
            e.Handled = true;
            await CopyColorAsync(_currentColor?.Hex);
        }
    }

    private bool MatchesGesture(string action, Key key)
    {
        var gesture = _hotkeys.Get(action);
        if (HotkeyCatalog.IsUnbound(gesture))
        {
            return false;
        }

        if (!Enum.TryParse<Key>(gesture.Key, ignoreCase: true, out var expected) || expected != key)
        {
            return false;
        }

        return ToHotkeyModifiers(Keyboard.Modifiers) == gesture.Modifiers;
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        var value = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            value |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            value |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            value |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            value |= HotkeyModifiers.Win;
        }

        return value;
    }

    private async Task RunAsync(Func<Task<string>> flow)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            var message = await flow().ConfigureAwait(true);
            ShowStatus(message);
        }
        catch (Exception ex)
        {
            ShowStatus($"截图流程异常：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
            _busy = false;
            RefreshScreenshotHistory();
        }
    }

    /// <summary>
    /// 取色：先隐藏主窗口与贴图，再在后台等待覆盖层返回；结束后恢复窗口与贴图。
    /// 覆盖层运行在独立 STA 线程，因此这里不会阻塞 UI 线程。
    /// </summary>
    private async Task PickColorAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetBusy(true);

        var window = Window.GetWindow(this);
        var state = window?.WindowState ?? WindowState.Normal;
        _flow.SuspendPins();
        window?.Hide();
        ShowStatus("取色中：移动鼠标并单击拾取，Esc 或右键取消。");

        try
        {
            var sample = await Task.Run(() => _colorProbe.Pick()).ConfigureAwait(true);
            ApplyColor(sample);
            await _colorHistory.AddAsync(sample).ConfigureAwait(true);
            RenderHistory();
            ShowStatus($"已拾取 {sample.Hex}");
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消取色。");
        }
        catch (Exception ex)
        {
            ShowStatus($"取色失败：{ex.Message}");
        }
        finally
        {
            _flow.ResumePins();
            if (window is not null)
            {
                window.Show();
                window.WindowState = state;
                window.Activate();
            }

            SetBusy(false);
            _busy = false;
            Focus();
        }
    }

    private void ApplyColor(ColorSample sample, bool showCoordinates = true)
    {
        _currentColor = sample;
        CurrentColorSwatch.Background = new SolidColorBrush(Color.FromRgb(sample.R, sample.G, sample.B));
        CurrentHexText.Text = sample.Hex;
        CurrentRgbText.Text = showCoordinates
            ? $"RGB({sample.R}, {sample.G}, {sample.B}) · 屏幕坐标 ({sample.X}, {sample.Y})"
            : $"RGB({sample.R}, {sample.G}, {sample.B}) · 来自历史色板";
    }

    private async Task CopyColorAsync(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            ShowStatus("请先取色，再复制色值。");
            return;
        }

        if (!_clipboard.TryWritePlainText(hex))
        {
            ShowStatus("复制失败：剪贴板不可用。");
            return;
        }

        ShowStatus($"已复制 {hex}");
        await ShowToastAsync($"已复制 {hex}").ConfigureAwait(true);
    }

    /// <summary>
    /// OCR：区域截图 → 本地识别。识别期间隐藏主窗口与贴图，完成后恢复；
    /// 结果写入可编辑文本框，并显示耗时与字数。
    /// </summary>
    private async Task RunOcrAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!_ocr.Available)
        {
            OcrStatusText.Text = "当前系统缺少 OCR 语言包，OCR 不可用";
            ShowStatus("OCR 不可用：当前系统缺少 OCR 语言包。");
            return;
        }

        _busy = true;
        SetBusy(true);
        OcrStatusText.Text = "正在截图…";

        var window = Window.GetWindow(this);
        var state = window?.WindowState ?? WindowState.Normal;
        _flow.SuspendPins();
        window?.Hide();

        try
        {
            var capture = await _capture.CaptureRegionAsync().ConfigureAwait(true);
            OcrStatusText.Text = "正在识别…";

            var stopwatch = Stopwatch.StartNew();
            var text = await _ocr.RecognizeAsync(capture.ImageBytes).ConfigureAwait(true);
            stopwatch.Stop();

            OcrResultBox.Text = text;
            OcrStatusText.Text = text.Length == 0
                ? $"未识别到文字 · {stopwatch.ElapsedMilliseconds} ms"
                : $"识别完成 · {text.Length} 字 · {stopwatch.ElapsedMilliseconds} ms";
            ShowStatus(text.Length == 0 ? "OCR 完成，未识别到文字。" : "OCR 识别完成。");
        }
        catch (OperationCanceledException)
        {
            OcrStatusText.Text = "已取消";
            ShowStatus("已取消 OCR。");
        }
        catch (Exception ex)
        {
            OcrStatusText.Text = "识别失败";
            ShowStatus($"OCR 失败：{ex.Message}");
        }
        finally
        {
            _flow.ResumePins();
            if (window is not null)
            {
                window.Show();
                window.WindowState = state;
                window.Activate();
            }

            SetBusy(false);
            _busy = false;
            Focus();
        }
    }

    private async Task CopyOcrAsync()
    {
        var text = OcrResultBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowStatus("没有可复制的识别结果。");
            return;
        }

        if (!_clipboard.TryWritePlainText(text))
        {
            ShowStatus("复制失败：剪贴板不可用。");
            return;
        }

        ShowStatus("已复制识别结果。");
        await ShowToastAsync($"已复制识别结果（{text.Length} 字）").ConfigureAwait(true);
    }

    private void RenderHistory()
    {
        HistoryPanel.Children.Clear();

        var colors = _colorHistory.Load();
        if (colors.Count == 0)
        {
            HistoryPanel.Children.Add(new TextBlock
            {
                Text = "还没有取色记录。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
            });
            return;
        }

        foreach (var color in colors)
        {
            // 用 Button 而非 Border：WPF 只为控件生成自动化 peer，色块才能被键盘/读屏访问。
            var swatch = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{color.Hex}（单击复制）",
                Tag = color,
                Style = (Style)FindResource("Theme.Button.Ghost"),
            };

            AutomationProperties.SetName(swatch, $"历史色块 {color.Hex}");
            swatch.Click += OnHistoryColorClick;
            HistoryPanel.Children.Add(swatch);
        }
    }

    /// <summary>单击历史色块：预览切换到该颜色，同时复制其色值并弹出轻提示。</summary>
    private async void OnHistoryColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ColorSample color })
        {
            return;
        }

        ApplyColor(color, showCoordinates: false);
        await CopyColorAsync(color.Hex);
    }

    /// <summary>刷新截图历史：按用户配置的保留张数读取记录并加载缩略图。</summary>
    private void RefreshScreenshotHistory()
    {
        var records = _screenshotStore.Recent();
        _historyItems = records
            .Select(record =>
            {
                var thumbRelative = record.ThumbPath ?? record.FilePath;
                return new ScreenshotHistoryItem(
                    record.Id,
                    LoadImage(_screenshotStore.ResolvePath(thumbRelative)),
                    $"{record.CreatedAt:MM-dd HH:mm} · {CaptureTypeLabel(record.CaptureType)}",
                    record.HasOriginal
                        ? $"{record.Width} × {record.Height}"
                        : $"{record.Width} × {record.Height} · 仅缩略图",
                    _screenshotStore.ResolvePath(record),
                    record.HasOriginal)
                {
                    ShowCheckBox = _selectionMode,
                };
            })
            .ToList();

        ScreenshotHistoryPanel.ItemsSource = _historyItems;
        ScreenshotHistoryPanel.Visibility = _historyItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryScroll.Visibility = _historyItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyText.Visibility = _historyItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_historyItems.Count == 0 && _selectionMode)
        {
            SetSelectionMode(false);
        }

        UpdateSelectionUi();

        // 三行高度依赖实际行高（字号可随主题变化），等布局完成后再量一次。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateHistoryViewport));
    }

    /// <summary>历史区固定为三行卡片高度，超过三行在本区域内滚动。</summary>
    private void UpdateHistoryViewport()
    {
        if (ScreenshotHistoryPanel.Items.Count == 0
            || ScreenshotHistoryPanel.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement container
            || container.ActualHeight <= 0)
        {
            return;
        }

        HistoryScroll.MaxHeight = Math.Ceiling(container.ActualHeight * 3) + 1;
    }

    /// <summary>进入 / 退出多选删除模式：只在模式内显示复选框与「全选」「删除」。</summary>
    private void SetSelectionMode(bool enabled)
    {
        _selectionMode = enabled;
        MultiSelectButton.Content = enabled ? "取消删除" : "多选删除";
        SelectAllButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DeleteSelectedButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in _historyItems)
        {
            item.IsSelected = false;
            item.ShowCheckBox = enabled;
        }

        UpdateSelectionUi();
    }

    private void OnMultiSelectClick(object sender, RoutedEventArgs e) => SetSelectionMode(!_selectionMode);

    private void UpdateSelectionUi()
    {
        var selected = _historyItems.Count(item => item.IsSelected);
        MultiSelectButton.IsEnabled = _historyItems.Count > 0;
        SelectAllButton.IsEnabled = _historyItems.Count > 0;
        SelectAllButton.Content = _historyItems.Count > 0 && selected == _historyItems.Count
            ? "取消全选"
            : "全选";
        DeleteSelectedButton.IsEnabled = selected > 0;

        var limit = _screenshotStore.RetentionLimit();
        HistorySummaryText.Text = _historyItems.Count == 0
            ? $"保留最近 {limit} 张，超出后自动清理最旧记录。"
            : _selectionMode
                ? $"已选 {selected} / {_historyItems.Count} 张 · 勾选后点「删除」一次清理"
                : $"最近 {_historyItems.Count} 张 · 上限 {limit} 张，超出后自动清理最旧记录。";
    }

    private void OnScreenshotSelectionChanged(object sender, RoutedEventArgs e) => UpdateSelectionUi();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (_historyItems.Count == 0)
        {
            return;
        }

        var selectAll = _historyItems.Any(item => !item.IsSelected);
        foreach (var item in _historyItems)
        {
            item.IsSelected = selectAll;
        }

        UpdateSelectionUi();
    }

    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _historyItems.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var choice = MessageBox.Show(
            Window.GetWindow(this),
            $"确定删除所选 {selected.Count} 张截图？文件将一并删除，且不可恢复。",
            "拾光",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleted = selected.Count(item => _screenshotStore.Delete(item.Id));
            RefreshScreenshotHistory();

            if (deleted == 0)
            {
                ShowStatus("删除失败：记录已不存在。");
                return;
            }

            ShowStatus($"已删除 {deleted} 张截图");
            await ShowToastAsync($"已删除 {deleted} 张截图").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowStatus($"删除失败：{ex.Message}");
            RefreshScreenshotHistory();
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string CaptureTypeLabel(string captureType) => captureType switch
    {
        "region" => "区域",
        "full" => "全屏",
        "window" => "窗口",
        "delay" => "延时",
        _ => captureType,
    };

    private void OnScreenshotThumbnailClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScreenshotHistoryItem item })
        {
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            ShowStatus("截图文件已不存在，已刷新历史。");
            RefreshScreenshotHistory();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus($"打开截图失败：{ex.Message}");
        }
    }

    private async void OnDeleteScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScreenshotHistoryItem item })
        {
            return;
        }

        var choice = MessageBox.Show(
            Window.GetWindow(this),
            "确定删除这张截图？文件将一并删除，且不可恢复。",
            "拾光",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (_screenshotStore.Delete(item.Id))
            {
                RefreshScreenshotHistory();
                await ShowToastAsync("已删除截图");
            }
            else
            {
                ShowStatus("删除失败：记录已不存在。");
                RefreshScreenshotHistory();
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"删除失败：{ex.Message}");
            RefreshScreenshotHistory();
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_screenshotStore.StorageDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_screenshotStore.StorageDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"打开存储目录失败：{ex.Message}");
        }
    }

    private async void OnThumbOnlyChanged(object sender, RoutedEventArgs e)
    {
        var enabled = ThumbOnlyCheck.IsChecked == true;
        _settings.Set(PreferenceKeys.ScreenshotThumbOnly, enabled);
        await _settings.SaveAsync().ConfigureAwait(true);
        await ShowToastAsync(enabled ? "已开启：后续截图仅保留缩略图" : "已恢复：后续截图保留原图")
            .ConfigureAwait(true);
    }

    /// <summary>底部轻提示：淡入、停留 1.5 秒、淡出；连续提示时重置计时。</summary>
    private async Task ShowToastAsync(string message)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        var cts = new CancellationTokenSource();
        _toastCts = cts;

        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        try
        {
            await Task.Delay(1500, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!cts.IsCancellationRequested)
            {
                ToastHost.Visibility = Visibility.Collapsed;
            }
        };
        ToastHost.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void SetBusy(bool busy)
    {
        RegionButton.IsEnabled = !busy;
        FullScreenButton.IsEnabled = !busy;
        WindowButton.IsEnabled = !busy;
        DelayedButton.IsEnabled = !busy;
        PickColorButton.IsEnabled = !busy;
        CopyColorButton.IsEnabled = !busy;
        OcrButton.IsEnabled = !busy && _ocr.Available;
        CopyOcrButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = $"{DateTime.Now:HH:mm:ss} · {message}";
        StatusText.Visibility = Visibility.Visible;
    }
}
