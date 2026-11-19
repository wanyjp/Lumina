using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// 单条剪贴板卡片：预览、展开详情、滑动露出编辑/删除、多选勾选。
/// 所有操作通过事件交给 <see cref="ClipboardView"/> 处理，卡片本身不访问服务。
/// </summary>
public partial class ClipboardCard : UserControl
{
    /// <summary>滑动露出的操作区宽度：两个 62px 按钮。</summary>
    private const double SwipeRevealWidth = 124;

    /// <summary>单击与双击的判定窗口；略大于系统双击间隔，避免误判。</summary>
    private static readonly TimeSpan ClickDelay = TimeSpan.FromMilliseconds(300);

    private readonly ClipboardItem _item;
    private readonly bool _selectionMode;
    private readonly bool _tagsVisible;

    private Point _dragStart;
    private double _swipeOffset;
    private bool _dragging;
    private bool _suppressSingleClick;
    private DispatcherTimer? _clickTimer;

    public ClipboardCard(
        ClipboardItem item,
        bool expanded,
        bool selectionMode,
        bool selected,
        bool tagsVisible)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        _selectionMode = selectionMode;
        _tagsVisible = tagsVisible;

        InitializeComponent();
        Build(item, expanded, selectionMode, selected);

        // ScrollContentPresenter 会在 Preview 阶段标记鼠标事件已处理以实现“点击聚焦”，
        // 因此必须用 handledEventsToo 注册，否则卡片永远收不到点击；按钮与勾选框由
        // IsInteractiveSource 排除，避免误触发展开。
        Surface.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnSurfaceMouseDown),
            handledEventsToo: true);
        Surface.AddHandler(
            UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnSurfaceMouseMove),
            handledEventsToo: true);
        Surface.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnSurfaceMouseUp),
            handledEventsToo: true);
        Surface.Cursor = Cursors.Hand;
        // 列表重建会卸载卡片：只停计时器，不能清 _suppressSingleClick，
        // 否则双击后的第二次抬手会重新启动单击计时器，把详情误展开。
        Unloaded += (_, _) => _clickTimer?.Stop();
    }

    public event EventHandler? CopyRequested;

    /// <summary>双击回填；与按钮复制分开，视图据此抑制列表重建后的误展开。</summary>
    public event EventHandler? PasteBackRequested;

    public event EventHandler? CopyPlainRequested;

    public event EventHandler? PinRequested;

    public event EventHandler? EditRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? SelectionToggled;

    public event EventHandler? ExpandToggled;

    public ClipboardItem Item => _item;

    /// <summary>键盘焦点指示；仅在列表内移动时显示。</summary>
    public void SetFocused(bool focused) =>
        FocusRing.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>把卡片滚动到可视区域，供 ↑↓ 导航使用。</summary>
    public void BringIntoViewSafely() => Dispatcher.BeginInvoke(
        new Action(() => BringIntoView()),
        System.Windows.Threading.DispatcherPriority.Loaded);

    private void Build(ClipboardItem item, bool expanded, bool selectionMode, bool selected)
    {
        KindText.Text = KindLabel(item.Kind);
        KindText.Foreground = (Brush)FindResource("Theme.AccentBrush");
        PinChip.Visibility = item.Pinned ? Visibility.Visible : Visibility.Collapsed;

        MetaText.Text = BuildMeta(item);

        var sources = ResolveSources(item);
        SourcesButton.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SourcesButton.Content = $"来源 ×{sources.Count}";
        SourcesButton.ToolTip = "查看全部来源";

        PreviewText.Text = item.Kind == ClipboardKind.Image
            ? item.Width is { } width && item.Height is { } height
                ? $"图片 {width} × {height}"
                : "图片"
            : ClipboardPlainText.Preview(item, 220);

        if (PreviewText.Text.Length == 0)
        {
            PreviewText.Text = "（无文本内容）";
            PreviewText.Foreground = (Brush)FindResource("Theme.TextTertiaryBrush");
        }

        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            NoteText.Text = "备注：" + item.Note.Trim();
            NoteText.Visibility = Visibility.Visible;
        }

        if (_tagsVisible && item.Tags.Count > 0)
        {
            TagPanel.Visibility = Visibility.Visible;
            foreach (var tag in item.Tags)
            {
                TagPanel.Children.Add(BuildTagChip(tag));
            }
        }

        SelectCheck.Visibility = selectionMode ? Visibility.Visible : Visibility.Collapsed;
        SelectCheck.IsChecked = selected;
        SelectCheck.IsEnabled = !item.Pinned;
        SelectCheck.ToolTip = item.Pinned ? "固定条目需先取消固定才能删除" : "选择此条目";

        ExpandGlyph.Text = expanded ? "\uE70E" : "\uE70D";

        PinButton.Content = item.Pinned ? "取消固定" : "固定";
        CopyPlainButton.IsEnabled = item.Kind != ClipboardKind.Image;
        CopyPlainButton.ToolTip = item.Kind == ClipboardKind.Image
            ? "图片没有可复制的纯文本"
            : "复制清洗后的纯文本内容";

        if (expanded)
        {
            DetailPanel.Visibility = Visibility.Visible;
            BuildDetail(item);
        }
    }

    private static string KindLabel(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Link => "链接",
        ClipboardKind.Html => "HTML",
        ClipboardKind.Image => "图片",
        ClipboardKind.File => "文件",
        _ => "文本",
    };

    private static IReadOnlyList<string> ResolveSources(ClipboardItem item)
    {
        if (item.Sources.Count > 0)
        {
            return item.Sources;
        }

        return string.IsNullOrWhiteSpace(item.SourceWindow)
            ? Array.Empty<string>()
            : [item.SourceWindow.Trim()];
    }

    private void OnSourcesClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var sources = ResolveSources(_item);
        if (sources.Count == 0)
        {
            return;
        }

        var dialog = new ClipboardSourcesWindow(sources)
        {
            Owner = Window.GetWindow(this),
        };
        dialog.ShowDialog();
    }

    private static string BuildMeta(ClipboardItem item)
    {
        var parts = new List<string>
        {
            FormatTime(item.UpdatedAt),
        };

        if (!string.IsNullOrWhiteSpace(item.SourceWindow))
        {
            parts.Add(item.SourceWindow.Trim());
        }

        if (item.DuplicateCount > 1)
        {
            parts.Add($"×{item.DuplicateCount}");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatTime(DateTimeOffset value)
    {
        var local = value.LocalDateTime;
        return local.Date == DateTime.Today
            ? local.ToString("HH:mm")
            : local.ToString("MM-dd HH:mm");
    }

    private Border BuildTagChip(string tag)
    {
        var chip = new Border
        {
            Style = (Style)FindResource("Theme.Chip"),
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(0, 0, 6, 4),
        };

        chip.Child = new TextBlock
        {
            Text = tag,
            FontSize = 11,
            Foreground = (Brush)FindResource("Theme.AccentSecondaryBrush"),
        };

        return chip;
    }

    /// <summary>按类型构建详情：长文本可滚动、HTML 富文本、图片缩略图、文件路径列表。</summary>
    private void BuildDetail(ClipboardItem item)
    {
        switch (item.Kind)
        {
            case ClipboardKind.Image:
                DetailHost.Content = BuildImageDetail(item);
                break;
            case ClipboardKind.Html:
                DetailHost.Content = BuildHtmlDetail(item);
                DetailHint.Text = "已按白名单清洗：不执行脚本、不加载外部资源。";
                DetailHint.Visibility = Visibility.Visible;
                break;
            case ClipboardKind.File:
                DetailHost.Content = BuildFileDetail(item);
                break;
            default:
                DetailHost.Content = BuildTextDetail(item.TextContent ?? string.Empty, mono: false);
                break;
        }
    }

    private FrameworkElement BuildTextDetail(string text, bool mono)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Theme.TextBrush"),
        };

        if (mono)
        {
            block.FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono");
        }

        return new ScrollViewer
        {
            Content = block,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private FrameworkElement BuildHtmlDetail(ClipboardItem item)
    {
        var model = ClipboardHtmlSanitizer.Sanitize(item.TextContent);
        if (model.IsEmpty)
        {
            return BuildTextDetail("（HTML 内容为空或已全部被白名单过滤）", mono: false);
        }

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = (FontFamily)FindResource("Theme.FontFamily"),
            FontSize = (double)FindResource("Theme.FontSize.Body"),
            Foreground = (Brush)FindResource("Theme.TextBrush"),
        };

        var body = (Brush)FindResource("Theme.TextBrush");
        var secondary = (Brush)FindResource("Theme.TextSecondaryBrush");
        var accent = (Brush)FindResource("Theme.AccentBrush");
        var mono = (FontFamily)FindResource("Theme.FontFamilyMono");

        foreach (var block in model.Blocks)
        {
            var paragraph = new Paragraph
            {
                Margin = block.Kind == HtmlBlockKind.ListItem
                    ? new Thickness(8 + (Math.Max(1, block.Level) - 1) * 12, 0, 0, 3)
                    : new Thickness(0, 0, 0, 6),
            };

            if (block.Kind == HtmlBlockKind.Heading)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize += 2;
            }
            else if (block.Kind == HtmlBlockKind.Quote)
            {
                paragraph.Foreground = secondary;
                paragraph.Margin = new Thickness(10, 0, 0, 6);
            }
            else if (block.Kind == HtmlBlockKind.Preformatted)
            {
                paragraph.FontFamily = mono;
            }

            if (block.Kind == HtmlBlockKind.ListItem)
            {
                paragraph.Inlines.Add(new Run("· "));
            }

            foreach (var run in block.Runs)
            {
                var inline = new Run(run.Text)
                {
                    FontWeight = run.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontStyle = run.Italic ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = string.IsNullOrEmpty(run.Href) ? body : accent,
                };

                if (run.Underline)
                {
                    inline.TextDecorations = TextDecorations.Underline;
                }

                if (run.Code)
                {
                    inline.FontFamily = mono;
                }

                paragraph.Inlines.Add(inline);
            }

            document.Blocks.Add(paragraph);
        }

        var viewer = new RichTextBox
        {
            Document = document,
            IsReadOnly = true,
            IsDocumentEnabled = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            IsReadOnlyCaretVisible = false,
        };

        if (model.Truncated)
        {
            DetailHint.Text = "内容较长，仅显示前 " + ClipboardHtmlSanitizer.MaxBlocks + " 段。"
                + "已按白名单清洗：不执行脚本、不加载外部资源。";
        }

        return viewer;
    }

    private FrameworkElement BuildImageDetail(ClipboardItem item)
    {
        var path = ClipboardMedia.ResolvePath(item.ThumbPath) ?? ClipboardMedia.ResolvePath(item.FilePath);
        if (path is null)
        {
            return BuildTextDetail("图片文件已不存在（可能已被清理）。", mono: false);
        }

        var bitmap = new BitmapImage();
        try
        {
            using var stream = File.OpenRead(path);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return BuildTextDetail("图片加载失败：" + ex.Message, mono: false);
        }

        var panel = new StackPanel();
        panel.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxHeight = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"{item.Width ?? bitmap.PixelWidth} × {item.Height ?? bitmap.PixelHeight} · PNG",
            Style = (Style)FindResource("Theme.Text.Tertiary"),
            Margin = new Thickness(0, 6, 0, 0),
        });

        return panel;
    }

    private FrameworkElement BuildFileDetail(ClipboardItem item)
    {
        var paths = ClipboardPlainText.FilePaths(item);
        if (paths.Count == 0)
        {
            return BuildTextDetail("（文件路径缺失）", mono: false);
        }

        var panel = new StackPanel();
        var caption = new TextBlock
        {
            Text = $"{paths.Count} 个文件",
            Style = (Style)FindResource("Theme.Text.Tertiary"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(caption);

        foreach (var path in paths)
        {
            panel.Children.Add(new TextBlock
            {
                Text = path,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono"),
                FontSize = (double)FindResource("Theme.FontSize.Caption"),
                Foreground = File.Exists(path)
                    ? (Brush)FindResource("Theme.TextBrush")
                    : (Brush)FindResource("Theme.TextTertiaryBrush"),
                Margin = new Thickness(0, 0, 0, 3),
            });
        }

        return panel;
    }

    // ---- 滑动与点击 ----

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        // 双击 = 回填到系统剪贴板：立即执行，并撤销上一次单击排队的展开动作。
        if (e.ClickCount >= 2)
        {
            _clickTimer?.Stop();
            _suppressSingleClick = true;
            PasteBackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragging = false;
        Surface.CaptureMouse();
    }

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Surface.IsMouseCaptured)
        {
            return;
        }

        var delta = e.GetPosition(this).X - _dragStart.X;
        if (!_dragging && Math.Abs(delta) < 4)
        {
            return;
        }

        _dragging = true;
        ActionLayer.Visibility = Visibility.Visible;

        var offset = Math.Clamp(_swipeOffset + delta, -SwipeRevealWidth, 0);
        SwipeTransform.X = offset;
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Surface.IsMouseCaptured)
        {
            Surface.ReleaseMouseCapture();
        }

        if (IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        if (_dragging)
        {
            // 超过一半回弹打开，否则收起。
            _swipeOffset = SwipeTransform.X < -SwipeRevealWidth / 2 ? -SwipeRevealWidth : 0;
            SwipeTransform.X = _swipeOffset;

            if (_swipeOffset == 0)
            {
                ActionLayer.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_selectionMode)
        {
            SelectionToggled?.Invoke(this, EventArgs.Empty);
            return;
        }

        // 单击延迟 300ms：若期间发生第二次点击（双击回填），则不再展开详情。
        if (_suppressSingleClick)
        {
            _suppressSingleClick = false;
            return;
        }

        StartClickTimer();
    }

    private void StartClickTimer()
    {
        _clickTimer ??= CreateClickTimer();
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private DispatcherTimer CreateClickTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = ClickDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_suppressSingleClick)
            {
                ExpandToggled?.Invoke(this, EventArgs.Empty);
            }

            _suppressSingleClick = false;
        };
        return timer;
    }

    private void ResetSwipe()
    {
        _swipeOffset = 0;
        SwipeTransform.X = 0;
        ActionLayer.Visibility = Visibility.Collapsed;
    }

    /// <summary>点击是否落在按钮、勾选框或输入框上；这些控件自行处理点击。</summary>
    private bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null && !ReferenceEquals(current, this))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                || current is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnSelectClick(object sender, RoutedEventArgs e)
    {
        // 勾选状态以视图维护的集合为准，卡片只上报意图，随后由视图整体重建。
        SelectionToggled?.Invoke(this, EventArgs.Empty);
    }

    private void OnSwipeEditClick(object sender, RoutedEventArgs e)
    {
        ResetSwipe();
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSwipeDeleteClick(object sender, RoutedEventArgs e)
    {
        ResetSwipe();
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyPlainClick(object sender, RoutedEventArgs e) =>
        CopyPlainRequested?.Invoke(this, EventArgs.Empty);

    private void OnPinClick(object sender, RoutedEventArgs e) => PinRequested?.Invoke(this, EventArgs.Empty);

    private void OnEditClick(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
