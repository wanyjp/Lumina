using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina.Domain.Capture;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Capture;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Capture;

/// <summary>
/// 编辑器状态快照：当前底图 + 未栅格化的矢量标注。
/// 裁剪/旋转会改变底图，因此与标注编辑共用一条撤销链。
/// </summary>
public sealed record EditorSnapshot(
    BitmapSource Image,
    IReadOnlyList<AnnotationElement> Elements,
    string Hint);

/// <summary>编辑器结果：保存 / 复制 / 贴图三种出路，均携带最终栅格化图像。</summary>
public sealed class AnnotationEditorResult
{
    public bool Saved { get; init; }

    public string? SavedPath { get; init; }

    public bool Copied { get; init; }

    public bool Pinned { get; init; }

    public byte[]? PinnedPng { get; init; }
}

/// <summary>
/// 标注编辑器：矢量标注 + 对象级撤销重做 + 裁剪/旋转 + 保存 / 复制 / 贴图。
/// 导出按当前底图尺寸栅格化；裁剪与旋转先把已有标注并入底图，可整体撤销。
/// </summary>
public partial class AnnotationEditorWindow : Window
{
    private const int CropMinimum = 8;

    private readonly IScreenshotStore _store;
    private readonly IClipboardWriter _clipboard;
    private readonly string _captureType;
    private readonly StateHistory<EditorSnapshot> _history;
    private readonly AnnotationCanvas _canvas;

    private AnnotationElement? _textEditing;
    private Point _textPoint;
    private bool _finished;

    private BitmapSource CurrentImage => _history.Current.Image;

    public AnnotationEditorResult Result { get; private set; } = new();

    public AnnotationEditorWindow(
        BitmapSource source,
        IScreenshotStore store,
        IClipboardWriter clipboard,
        string captureType)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureType);

        _store = store;
        _clipboard = clipboard;
        _captureType = captureType;
        _canvas = new AnnotationCanvas(source);
        _history = new StateHistory<EditorSnapshot>(
            new EditorSnapshot(source, [], "矢量标注导出时栅格化"));

        InitializeComponent();
        BuildSwatches();
        BuildFontSizes();
        LayoutCanvas();

        _canvas.ElementCreated += OnElementCreated;
        _canvas.ElementEdited += OnElementEdited;
        _canvas.SelectionChanged += (_, _) => UpdateActionState();
        _canvas.CropRectChanged += (_, _) => UpdateActionState();
        _canvas.TextPlacementRequested += OnTextPlacementRequested;
        _canvas.TextEditRequested += OnTextEditRequested;

        CanvasArea.Child = _canvas;

        InfoText.Text = $"{source.PixelWidth} × {source.PixelHeight} px · PNG · {_history.Current.Hint}";
        Title = "标注截图";
        UpdateActionState();
    }

    private void LayoutCanvas()
    {
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(320, workArea.Width * 0.74);
        var maxHeight = Math.Max(240, workArea.Height * 0.62);

        var image = CurrentImage;
        var scale = Math.Min(
            Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight),
            1.0);
        scale = Math.Max(0.04, scale);

        _canvas.Width = Math.Max(40, image.PixelWidth * scale);
        _canvas.Height = Math.Max(30, image.PixelHeight * scale);

        // 画布 + 边距 + 双行工具栏与动作条，收敛在工作区内。
        var chromeWidth = 72.0;
        var chromeHeight = 196.0;
        Width = Math.Min(Math.Max(_canvas.Width + chromeWidth, MinWidth), workArea.Width - 40);
        Height = Math.Min(Math.Max(_canvas.Height + chromeHeight, MinHeight), workArea.Height - 40);
    }

    private void BuildSwatches()
    {
        foreach (var (name, argb) in AnnotationPalette.All)
        {
            var color = Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));

            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 7, 0),
                ToolTip = name,
                Tag = argb,
                Uid = "swatch",
            };

            swatch.MouseLeftButtonUp += OnSwatchClick;
            SwatchPanel.Children.Add(swatch);
        }

        HighlightSwatch(AnnotationPalette.DefaultColor);
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: uint argb })
        {
            return;
        }

        _canvas.Color = argb;
        HighlightSwatch(argb);
        e.Handled = true;
    }

    private void HighlightSwatch(uint argb)
    {
        foreach (var child in SwatchPanel.Children)
        {
            if (child is Border border && border.Uid == "swatch" && border.Tag is uint value)
            {
                var selected = value == argb;
                border.BorderBrush = selected ? SystemColors.HighlightBrush : Brushes.Transparent;
                border.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }
    }

    private void BuildFontSizes()
    {
        foreach (var size in AnnotationDefaults.FontSizes)
        {
            FontSizeBox.Items.Add(size);
        }

        FontSizeBox.SelectedItem = AnnotationDefaults.FontMedium;
        CustomColorBox.Text = "#E84040";
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_canvas is null || FontSizeBox.SelectedItem is not double size)
        {
            return;
        }

        _canvas.FontSize = size;
    }

    private void OnCustomColorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplyCustomColor();
        }
    }

    private void OnApplyCustomColorClick(object sender, RoutedEventArgs e) => ApplyCustomColor();

    private void ApplyCustomColor()
    {
        var text = CustomColorBox.Text?.Trim() ?? string.Empty;
        if (!TryParseColor(text, out var argb))
        {
            System.Windows.MessageBox.Show(
                this,
                "颜色格式应为 #RRGGBB 或 #AARRGGBB，例如 #E84040。",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _canvas.Color = argb;
        HighlightSwatch(argb);
        FocusCanvas();
    }

    private static bool TryParseColor(string text, out uint argb)
    {
        argb = 0;
        if (!text.StartsWith('#'))
        {
            return false;
        }

        var digits = text[1..];
        if (digits.Length != 6 && digits.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(digits, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        argb = digits.Length == 6 ? 0xFF000000u | value : value;
        return true;
    }

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        _canvas.Tool = sender switch
        {
            _ when sender == ToolSelect => AnnotationTool.Select,
            _ when sender == ToolRectangle => AnnotationTool.Rectangle,
            _ when sender == ToolArrow => AnnotationTool.Arrow,
            _ when sender == ToolText => AnnotationTool.Text,
            _ when sender == ToolMosaic => AnnotationTool.Mosaic,
            _ when sender == ToolFreehand => AnnotationTool.Freehand,
            _ when sender == ToolEllipse => AnnotationTool.Ellipse,
            _ when sender == ToolLine => AnnotationTool.Line,
            _ when sender == ToolHighlight => AnnotationTool.Highlight,
            _ when sender == ToolCrop => AnnotationTool.Crop,
            _ => AnnotationTool.Select,
        };
        UpdateActionState();
        FocusCanvas();
    }

    /// <summary>工具快捷键：V/R/E/L/A/F/T/M/H/C。</summary>
    private void SelectToolByKey(Key key)
    {
        var button = key switch
        {
            Key.V => ToolSelect,
            Key.R => ToolRectangle,
            Key.E => ToolEllipse,
            Key.L => ToolLine,
            Key.A => ToolArrow,
            Key.F => ToolFreehand,
            Key.T => ToolText,
            Key.M => ToolMosaic,
            Key.H => ToolHighlight,
            Key.C => ToolCrop,
            _ => null,
        };

        if (button is not null)
        {
            button.IsChecked = true;
        }
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        _canvas.StrokeLevel = sender == SizeSmall ? 0 : sender == SizeMedium ? 1 : 2;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Redo();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void OnRotateLeftClick(object sender, RoutedEventArgs e) => Rotate(-90);

    private void OnRotateRightClick(object sender, RoutedEventArgs e) => Rotate(90);

    private void OnRotate180Click(object sender, RoutedEventArgs e) => Rotate(180);

    private void OnApplyCropClick(object sender, RoutedEventArgs e) => ApplyCrop();

    private void OnCancelCropClick(object sender, RoutedEventArgs e) => _canvas.ClearCropRect();

    private void Undo()
    {
        if (_history.Undo() is { } state)
        {
            ApplyState(state);
        }
    }

    private void Redo()
    {
        if (_history.Redo() is { } state)
        {
            ApplyState(state);
        }
    }

    /// <summary>
    /// 旋转当前底图。已有矢量标注先栅格化并入底图，旋转后标注集合清空，
    /// 但整步操作作为快照可撤销（撤销后底图与标注一并恢复）。
    /// </summary>
    private void Rotate(double angle)
    {
        var composed = ComposeBitmap();
        var rotated = new TransformedBitmap(composed, new RotateTransform(angle));
        rotated.Freeze();

        _canvas.ClearCropRect();
        ToolSelect.IsChecked = true;
        CommitImage(rotated, $"旋转 {angle:0}° · 已有标注已并入底图，可撤销");
    }

    /// <summary>按画布上的选区裁剪；坐标按当前底图像素钳制，避免越界。</summary>
    private void ApplyCrop()
    {
        if (_canvas.CropRect is not { } rect)
        {
            return;
        }

        var image = CurrentImage;
        var x = (int)Math.Round(Math.Clamp(rect.X, 0, image.PixelWidth - 1));
        var y = (int)Math.Round(Math.Clamp(rect.Y, 0, image.PixelHeight - 1));
        var width = (int)Math.Min(Math.Round(rect.Width), image.PixelWidth - x);
        var height = (int)Math.Min(Math.Round(rect.Height), image.PixelHeight - y);
        if (width < CropMinimum || height < CropMinimum)
        {
            MessageBox.Show(
                this,
                "裁剪区域过小，请重新框选。",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var composed = ComposeBitmap();
        var cropped = new CroppedBitmap(composed, new Int32Rect(x, y, width, height));
        cropped.Freeze();

        _canvas.ClearCropRect();
        ToolSelect.IsChecked = true;
        CommitImage(cropped, "裁剪已应用 · 已有标注已并入底图，可撤销");
    }

    private BitmapSource ComposeBitmap()
        => AnnotationRenderer.ComposeBitmap(
            CurrentImage,
            _history.Current.Elements,
            new MosaicRasterizer(CurrentImage));

    private void CommitImage(BitmapSource image, string hint)
    {
        var snapshot = new EditorSnapshot(image, [], hint);
        _history.Commit(snapshot);
        ApplyState(snapshot);
    }

    private void RemoveSelected()
    {
        if (_canvas.SelectedIndex < 0)
        {
            return;
        }

        var list = _history.Current.Elements.ToList();
        list.RemoveAt(_canvas.SelectedIndex);
        CommitElements(list, "删除标注");
    }

    private void OnElementCreated(object? sender, AnnotationElement element)
    {
        var list = _history.Current.Elements.ToList();
        list.Add(element);
        CommitElements(list, "新增标注");
        _canvas.SelectedIndex = list.Count - 1;
    }

    private void OnElementEdited(object? sender, AnnotationElement element)
    {
        if (_canvas.SelectedIndex < 0)
        {
            return;
        }

        var list = _history.Current.Elements.ToList();
        list[_canvas.SelectedIndex] = element;
        CommitElements(list, "编辑标注");
    }

    private void CommitElements(IReadOnlyList<AnnotationElement> elements, string hint)
    {
        var snapshot = new EditorSnapshot(CurrentImage, elements, hint);
        _history.Commit(snapshot);
        ApplyState(snapshot);
    }

    private void ApplyState(EditorSnapshot state)
    {
        _canvas.SetSource(state.Image);
        _canvas.Elements = state.Elements;
        LayoutCanvas();
        InfoText.Text = $"{state.Image.PixelWidth} × {state.Image.PixelHeight} px · PNG · {state.Hint}";
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        if (UndoButton is null || CropApplyButton is null)
        {
            return;
        }

        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        DeleteButton.IsEnabled = _canvas.SelectedElement is not null;

        var hasCrop = _canvas.Tool == AnnotationTool.Crop
            && _canvas.CropRect is { } rect
            && rect.Width >= CropMinimum
            && rect.Height >= CropMinimum;
        CropApplyButton.IsEnabled = hasCrop;
        CropCancelButton.IsEnabled = hasCrop;
    }

    private void FocusCanvas()
    {
        // 画布不接键盘；把焦点收回到窗口，保证 Esc / Delete / Ctrl+Z 生效。
        Focus();
    }

    private void OnTextPlacementRequested(object? sender, Point point)
    {
        BeginTextInput(point, null);
    }

    private void OnTextEditRequested(object? sender, AnnotationElement element)
    {
        BeginTextInput(new Point(element.X, element.Y), element);
    }

    private void BeginTextInput(Point imagePoint, AnnotationElement? editing)
    {
        _textPoint = imagePoint;
        _textEditing = editing;

        TextOverlay.Text = editing?.Text ?? string.Empty;
        TextOverlay.Visibility = Visibility.Visible;
        PositionTextOverlay();
        TextOverlay.Focus();
        TextOverlay.SelectAll();
    }

    private void PositionTextOverlay()
    {
        var scale = _canvas.Scale;
        var offset = _canvas.TranslatePoint(new Point(0, 0), CanvasHost);
        var left = offset.X + (_textPoint.X * scale);
        var top = offset.Y + (_textPoint.Y * scale);

        TextOverlay.Margin = new Thickness(
            Math.Max(4, left),
            Math.Max(4, top),
            0,
            0);
    }

    private void OnTextOverlayKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTextInput();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelTextInput();
        }
    }

    private void OnTextOverlayLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 焦点意外丢失（点击别处）时按提交处理，避免误丢已输入内容。
        if (TextOverlay.Visibility == Visibility.Visible && !_finished)
        {
            CommitTextInput();
        }
    }

    private void CommitTextInput()
    {
        var text = TextOverlay.Text?.Trim() ?? string.Empty;
        var editing = _textEditing;
        HideTextInput();

        if (text.Length == 0)
        {
            if (editing is not null)
            {
                // 清空即删除原元素。
                var index = IndexOfElement(editing);
                if (index >= 0)
                {
                    _canvas.SelectedIndex = index;
                    RemoveSelected();
                }
            }

            return;
        }

        var fontSize = editing?.FontSize ?? _canvas.FontSize;
        var measured = AnnotationRenderer.MeasureText(text, fontSize, 1.0);

        var element = new AnnotationElement
        {
            Kind = AnnotationKind.Text,
            X = _textPoint.X,
            Y = _textPoint.Y,
            Width = Math.Max(4, measured.Width),
            Height = Math.Max(4, measured.Height),
            Color = editing?.Color ?? _canvas.Color,
            Stroke = 0,
            Text = text,
            FontSize = fontSize,
        };

        if (editing is not null)
        {
            var index = IndexOfElement(editing);
            if (index >= 0)
            {
                _canvas.SelectedIndex = index;
                OnElementEdited(this, element);
                return;
            }
        }

        OnElementCreated(this, element);
    }

    /// <summary>按记录值相等查找元素下标；找不到返回 -1。</summary>
    private int IndexOfElement(AnnotationElement element)
    {
        var current = _history.Current.Elements;
        for (var index = 0; index < current.Count; index++)
        {
            if (Equals(current[index], element))
            {
                return index;
            }
        }

        return -1;
    }

    private void CancelTextInput()
    {
        HideTextInput();
        FocusCanvas();
    }

    private void HideTextInput()
    {
        TextOverlay.Visibility = Visibility.Collapsed;
        _textEditing = null;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (TextOverlay.Visibility == Visibility.Visible
            || Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_canvas.CropRect is not null)
            {
                _canvas.ClearCropRect();
            }
            else if (_canvas.Tool == AnnotationTool.Crop)
            {
                ToolSelect.IsChecked = true;
            }
            else
            {
                RequestClose();
            }
        }
        else if (e.Key == Key.Enter && _canvas.Tool == AnnotationTool.Crop && _canvas.CropRect is not null)
        {
            e.Handled = true;
            ApplyCrop();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            RemoveSelected();
        }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Redo();
            }
            else
            {
                Undo();
            }
        }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            Redo();
        }
        else if (_canvas.SelectedElement is not null
                 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                ? AnnotationDefaults.NudgeLargeStep
                : AnnotationDefaults.NudgeStep;
            var (dx, dy) = e.Key switch
            {
                Key.Left => (-step, 0d),
                Key.Right => (step, 0d),
                Key.Up => (0d, -step),
                _ => (0d, step),
            };
            _canvas.NudgeSelected(dx, dy);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            // 单字母工具快捷键（不在输入框内时才生效）。
            SelectToolByKey(e.Key);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var png = Compose();
        try
        {
            var record = _store.Save(
                png,
                _captureType,
                CurrentImage.PixelWidth,
                CurrentImage.PixelHeight,
                DateTimeOffset.Now);

            Result = new AnnotationEditorResult
            {
                Saved = true,
                SavedPath = _store.ResolvePath(record),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"保存截图失败：{ex.Message}\n可改用“复制”或“贴图”，稍后再试。",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Finish();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var png = Compose();
        var tempPath = Path.Combine(Path.GetTempPath(), $"lumina-paste-{DateTime.Now:yyyyMMddHHmmssfff}.png");
        try
        {
            File.WriteAllBytes(tempPath, png);
            var item = new ClipboardItem
            {
                Kind = ClipboardKind.Image,
                FilePath = tempPath,
                Width = CurrentImage.PixelWidth,
                Height = CurrentImage.PixelHeight,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            };

            if (!_clipboard.TryWrite(item))
            {
                MessageBox.Show(
                    this,
                    "复制到剪贴板失败：剪贴板可能被其他程序占用。\n可改用“保存”，或稍后重试。",
                    "拾光",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Result = new AnnotationEditorResult { Copied = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"写入临时文件失败：{ex.Message}\n可改用“保存”。",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // 临时文件清理失败不影响主流程，系统重启目录自清。
            }
        }

        Finish();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        var png = Compose();
        Result = new AnnotationEditorResult { Pinned = true, PinnedPng = png };
        Finish();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => RequestClose();

    private void RequestClose()
    {
        if (_history.CanUndo)
        {
            var choice = MessageBox.Show(
                this,
                "已有编辑尚未导出，关闭将丢弃全部修改。确定关闭？",
                "拾光",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Result = new AnnotationEditorResult();
        Finish();
    }

    private byte[] Compose()
        => AnnotationRenderer.ComposePng(
            CurrentImage,
            _history.Current.Elements,
            new MosaicRasterizer(CurrentImage));

    private void Finish()
    {
        _finished = true;
        HideTextInput();
        Close();
    }
}
