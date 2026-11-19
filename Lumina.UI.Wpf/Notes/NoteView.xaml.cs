using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Services.Contracts;
using Lumina.Services.Notes;

namespace Lumina.UI.Wpf.Notes;

/// <summary>
/// P3-05 便签速记：卡片列表 + 编辑窗。卡片展示 Markdown 预览的前若干块，
/// 置顶/删除就地操作；换色、清单转换与自动保存在编辑窗内完成。
/// </summary>
public partial class NoteView : UserControl
{
    private readonly INoteService _notes;
    private readonly ITodoService _todos;
    private readonly IWidgetHost _widgetHost;
    private IReadOnlyList<NoteItem> _items = [];
    private bool _disposed;
    private bool _loading;

    public NoteView(INoteService notes, ITodoService todos, IWidgetHost widgetHost)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(widgetHost);

        _notes = notes;
        _todos = todos;
        _widgetHost = widgetHost;
        InitializeComponent();

        Zoom.EditRequested += OnZoomEdit;
        Zoom.DesktopRequested += OnZoomDesktop;
        Zoom.DeleteRequested += OnZoomDelete;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>全局热键 Ctrl+Alt+N：切到便签页并直接打开新建编辑窗。</summary>
    public void CreateNote() => OnAddClick(this, new RoutedEventArgs());

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 视图可能被主壳缓存复用：每次进入都恢复可用并重新挂事件。
        _disposed = false;
        _notes.Changed -= OnNotesChanged;
        _notes.Changed += OnNotesChanged;
        await ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _notes.Changed -= OnNotesChanged;
        _disposed = true;
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
        if (_disposed || _loading)
        {
            return;
        }

        _loading = true;
        try
        {
            _items = await _notes.AllAsync();
            RenderCards();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"加载失败：{exception.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void RenderCards()
    {
        CardGrid.Children.Clear();
        foreach (var note in _items)
        {
            CardGrid.Children.Add(BuildCard(note));
        }

        SummaryLine.Text = $"共 {_items.Count} 条 · 输入后自动保存";
        EmptyPanel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardGrid.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement BuildCard(NoteItem note)
    {
        var palette = NotePalette.Resolve(note.Color);
        var card = new Border
        {
            Style = (Style)FindResource("Theme.Card"),
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14),
        };

        var stack = new StackPanel();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        header.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Hex)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.Title) ? "无标题" : note.Title,
            FontSize = (double)FindResource("Theme.FontSize.Body"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            string.IsNullOrWhiteSpace(note.Title) ? "Theme.TextTertiaryBrush" : "Theme.TextBrush");
        header.Children.Add(title);

        if (note.Pinned)
        {
            var pinned = new TextBlock
            {
                Text = "置顶",
                FontSize = (double)FindResource("Theme.FontSize.Caption"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            pinned.SetResourceReference(TextBlock.ForegroundProperty, "Theme.AccentBrush");
            header.Children.Add(pinned);
        }

        var previewHost = new Border
        {
            MaxHeight = 148,
            ClipToBounds = true,
            Background = Brushes.Transparent,
        };
        previewHost.Child = NotePreviewRenderer.Build(NoteMarkdown.Parse(note.Content), compact: true);

        // 标题与预览一起作为打开编辑窗的热区，底部的置顶/删除不受影响。
        var body = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        AutomationProperties.SetName(
            body,
            "便签卡片 " + (string.IsNullOrWhiteSpace(note.Title) ? "无标题" : note.Title));
        body.Children.Add(header);
        body.Children.Add(previewHost);
        body.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Zoom.Show(note);
        };
        stack.Children.Add(body);

        stack.Children.Add(BuildFooter(note));

        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildFooter(NoteItem note)
    {
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };

        var meta = new StackPanel { Orientation = Orientation.Horizontal };
        var time = new TextBlock
        {
            Text = note.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            FontSize = (double)FindResource("Theme.FontSize.Caption"),
        };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        meta.Children.Add(time);

        var (total, done) = ChecklistText.Count(note.Content);
        if (total > 0)
        {
            var progress = new TextBlock
            {
                Text = $"☑ {done}/{total}",
                FontSize = (double)FindResource("Theme.FontSize.Caption"),
                Margin = new Thickness(10, 0, 0, 0),
            };
            progress.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
            meta.Children.Add(progress);
        }

        DockPanel.SetDock(meta, Dock.Left);
        footer.Children.Add(meta);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var pin = SmallButton(note.Pinned ? "取消置顶" : "置顶");
        pin.Click += async (_, _) => await TogglePinAsync(note);
        actions.Children.Add(pin);

        var remove = SmallButton("删除");
        remove.Foreground = (Brush)FindResource("Theme.DangerBrush");
        remove.Click += async (_, _) => await DeleteAsync(note);
        actions.Children.Add(remove);

        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);

        return footer;
    }

    private Button SmallButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("Theme.Button.Ghost"),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = (double)FindResource("Theme.FontSize.Caption"),
            Margin = new Thickness(6, 0, 0, 0),
        };
        return button;
    }

    private async Task TogglePinAsync(NoteItem note)
    {
        try
        {
            await _notes.SaveAsync(note with { Pinned = !note.Pinned });
        }
        catch (Exception exception)
        {
            StatusText.Text = $"操作失败：{exception.Message}";
        }
    }

    private async Task DeleteAsync(NoteItem note)
    {
        var owner = Window.GetWindow(this);
        var result = owner is null
            ? MessageBox.Show(
                "删除后不可恢复，确定删除这条便签吗？",
                "删除便签",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel)
            : MessageBox.Show(
                owner,
                "删除后不可恢复，确定删除这条便签吗？",
                "删除便签",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _notes.DeleteAsync([note.Id]);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"删除失败：{exception.Message}";
        }
    }

    private void OpenEditor(NoteItem note)
    {
        var editor = new NoteEditorWindow(_notes, _todos, note);
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            editor.Owner = owner;
        }

        editor.ShowDialog();
        _ = ReloadAsync();
    }

    private void OnZoomEdit(object? sender, EventArgs e)
    {
        if (Zoom.Item is not { } note)
        {
            return;
        }

        Zoom.Close();
        OpenEditor(note);
    }

    private void OnZoomDesktop(object? sender, EventArgs e)
    {
        if (Zoom.Item is not { } note)
        {
            return;
        }

        try
        {
            var state = _widgetHost.GetState(WidgetKind.Note);
            _widgetHost.ShowWidget(state with { SourceId = note.Id });
            StatusText.Text = "已添加到桌面";
            Zoom.Close();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"添加到桌面失败：{exception.Message}";
        }
    }

    private async void OnZoomDelete(object? sender, EventArgs e)
    {
        if (Zoom.Item is not { } note)
        {
            return;
        }

        await DeleteAsync(note);
        if (_items.All(item => item.Id != note.Id))
        {
            Zoom.Close();
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.Now;
        OpenEditor(new NoteItem
        {
            Content = string.Empty,
            Color = NotePalette.DefaultKey,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
