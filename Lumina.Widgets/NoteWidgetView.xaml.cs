using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Services.Contracts;
using Lumina.Services.Notes;

namespace Lumina.Widgets;

/// <summary>
/// 便签桌面组件（P3-06）：展示绑定便签的标题、勾选进度与正文；
/// 清单项可直接点击切换，写回正文并持久化，主列表与编辑窗通过 <see cref="INoteService.Changed"/> 同步。
/// </summary>
public partial class NoteWidgetView : UserControl, IWidgetGlassView
{
    private readonly INoteService _notes;

    private long? _sourceId;
    private NoteItem? _note;
    private bool _subscribed;

    public NoteWidgetView(INoteService notes, long? sourceId)
    {
        ArgumentNullException.ThrowIfNull(notes);

        _notes = notes;
        _sourceId = sourceId;

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler? DragRequested;

    public event EventHandler? CloseRequested;

    /// <summary>内容已重建（窗口据此重新计算自适应高度）。</summary>
    public event Action? ContentChanged;

    public Border CardElement => Card;

    public WidgetGlassSurface GlassSurface => Glass;

    public void ApplySource(long? sourceId)
    {
        _sourceId = sourceId;
        _ = ReloadAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _notes.Changed += OnNotesChanged;
            _subscribed = true;
        }

        await ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _notes.Changed -= OnNotesChanged;
            _subscribed = false;
        }
    }

    private void OnNotesChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => _ = ReloadAsync()));
            return;
        }

        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_sourceId is not { } id)
        {
            _note = null;
            Render(null);
            return;
        }

        try
        {
            var notes = await _notes.AllAsync();
            _note = notes.FirstOrDefault(note => note.Id == id);
            Render(_note);
        }
        catch (Exception)
        {
            _note = null;
            Render(null);
        }
    }

    private void Render(NoteItem? note)
    {
        LinesPanel.Children.Clear();

        if (note is null)
        {
            TitleText.Text = "便签";
            MetaText.Text = _sourceId is null ? "未绑定便签" : "便签已删除";
            ColorDot.Fill = Brushes.Transparent;
            LinesPanel.Children.Add(CreateHint("在便签里点「添加到桌面」绑定一条便签。"));
            ContentChanged?.Invoke();
            return;
        }

        TitleText.Text = string.IsNullOrWhiteSpace(note.Title) ? "无标题" : note.Title;
        var palette = NotePalette.Resolve(note.Color);
        ColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Hex));

        var (total, done) = ChecklistText.Count(note.Content);
        MetaText.Text = total > 0
            ? $"☑ {done}/{total} · {note.UpdatedAt.ToLocalTime():MM-dd HH:mm}"
            : note.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");

        if (string.IsNullOrWhiteSpace(note.Content))
        {
            LinesPanel.Children.Add(CreateHint("空便签"));
            ContentChanged?.Invoke();
            return;
        }

        var checkLines = CollectCheckLines(note.Content);
        var checkIndex = 0;
        foreach (var block in NoteMarkdown.Parse(note.Content))
        {
            if (block.Kind == MarkdownBlockKind.Check)
            {
                var line = checkIndex < checkLines.Count ? checkLines[checkIndex] : -1;
                checkIndex++;
                LinesPanel.Children.Add(BuildCheckRow(note, block, line));
                continue;
            }

            LinesPanel.Children.Add(BuildBlock(block));
        }

        ContentChanged?.Invoke();
    }

    /// <summary>
    /// 计算容纳当前内容所需的高度：只测量正文 StackPanel，避免受 ScrollViewer
    /// 与窗口当前高度的相互约束影响；正文超过 360 后由滚动条承接。
    /// </summary>
    public double PreferredHeight()
    {
        var width = LinesPanel.ActualWidth > 0
            ? LinesPanel.ActualWidth
            : Math.Max(BodyScroll.ActualWidth, 240);

        LinesPanel.InvalidateMeasure();
        LinesPanel.Measure(new Size(width, double.PositiveInfinity));
        var body = Math.Min(LinesPanel.DesiredSize.Height, 360);

        var title = TitleText.ActualHeight > 0 ? TitleText.ActualHeight : 22;
        var meta = MetaText.ActualHeight > 0 ? MetaText.ActualHeight : 18;

        // 外层 Grid 10+10、卡片内边距 12+12、标题/元信息/正文间距。
        return 20 + 24 + title + 4 + meta + 7 + body;
    }

    private FrameworkElement BuildCheckRow(NoteItem note, MarkdownBlock block, int lineIndex)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var glyph = new TextBlock
        {
            Text = block.Checked ? ChecklistText.Checked : ChecklistText.Unchecked,
            FontSize = 14,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        glyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            block.Checked ? "Theme.AccentBrush" : "Theme.Widget.TextTertiaryBrush");
        row.Children.Add(glyph);

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        };
        text.SetResourceReference(
            TextBlock.ForegroundProperty,
            block.Checked ? "Theme.Widget.TextTertiaryBrush" : "Theme.Widget.TextBrush");
        AppendInlines(text, block.Inlines);
        row.Children.Add(text);

        var button = new Button
        {
            Style = (Style)FindResource("NoteWidget.CheckRow"),
            Content = row,
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = block.Checked ? "标记为未完成" : "标记为已完成",
        };
        AutomationProperties.SetName(button, block.Checked ? "取消勾选" : "勾选");
        button.Click += async (_, _) => await ToggleAsync(note, lineIndex);
        return button;
    }

    private FrameworkElement BuildBlock(MarkdownBlock block)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Widget.TextBrush");

        var prefix = block.Kind switch
        {
            MarkdownBlockKind.Bullet => "• ",
            MarkdownBlockKind.Ordered => $"{block.Number}. ",
            _ => string.Empty,
        };
        if (prefix.Length > 0)
        {
            text.Inlines.Add(new Run(prefix));
        }

        AppendInlines(text, block.Inlines);
        return text;
    }

    private TextBlock CreateHint(string message)
    {
        var hint = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Widget.TextTertiaryBrush");
        return hint;
    }

    private void AppendInlines(TextBlock target, IReadOnlyList<MarkdownInline> inlines)
    {
        var mono = (FontFamily)Application.Current.FindResource("Theme.FontFamilyMono");
        foreach (var inline in inlines)
        {
            var run = new Run(inline.Text);
            switch (inline.Kind)
            {
                case MarkdownInlineKind.Bold:
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case MarkdownInlineKind.Code:
                    run.FontFamily = mono;
                    run.FontSize = 13;
                    break;
            }

            target.Inlines.Add(run);
        }
    }

    private static List<int> CollectCheckLines(string content)
    {
        var result = new List<int>();
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (ChecklistText.TryParseLine(lines[index], out _, out _, out _))
            {
                result.Add(index);
            }
        }

        return result;
    }

    private async Task ToggleAsync(NoteItem note, int lineIndex)
    {
        if (lineIndex < 0 || !ChecklistText.TryToggle(note.Content, lineIndex, out var updated))
        {
            return;
        }

        try
        {
            await _notes.SaveAsync(note with { Content = updated });
        }
        catch (Exception)
        {
            // 保存失败保持原状，界面在下次 Changed 时回到数据库内容。
        }
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed
            || WidgetDragHelper.IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        DragRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
