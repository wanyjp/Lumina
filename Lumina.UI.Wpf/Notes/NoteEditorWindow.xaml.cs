using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Services.Contracts;
using Lumina.Services.Notes;

namespace Lumina.UI.Wpf.Notes;

/// <summary>
/// 便签编辑窗：标题 + 正文 + 色卡 + 编辑/预览切换 + 清单转换。
/// 输入后 800ms 防抖保存，关闭时再冲刷一次；失败在底部状态行用红字提示。
/// </summary>
public partial class NoteEditorWindow : Window
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(800);

    private readonly INoteService _notes;
    private readonly ITodoService _todos;
    private readonly DispatcherTimer _saveTimer;

    private NoteItem _note;
    private string _color;
    private bool _initializing;
    private bool _dirty;
    private bool _saving;
    private bool _pendingSave;
    private bool _closeRequested;
    private bool _closeAfterSave;

    public NoteEditorWindow(INoteService notes, ITodoService todos, NoteItem note)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(note);

        _notes = notes;
        _todos = todos;
        _note = note;
        _color = NotePalette.Normalize(note.Color);

        InitializeComponent();

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = SaveDebounce,
        };
        _saveTimer.Tick += OnSaveTimerTick;

        _initializing = true;
        TitleBox.Text = note.Title ?? string.Empty;
        ContentBox.Text = note.Content;
        _initializing = false;

        BuildColorSwatches();
        UpdateStatus("输入后自动保存");

        Closing += OnClosing;
        Loaded += (_, _) => ContentBox.Focus();
    }

    private void BuildColorSwatches()
    {
        ColorPanel.Children.Clear();
        foreach (var color in NotePalette.All)
        {
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Hex)),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(
                    string.Equals(color.Key, _color, StringComparison.Ordinal) ? 3 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = color.Name,
            };
            swatch.SetResourceReference(
                Border.BorderBrushProperty,
                string.Equals(color.Key, _color, StringComparison.Ordinal)
                    ? "Theme.AccentBrush"
                    : "Theme.BorderBrush");

            var key = color.Key;
            swatch.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (string.Equals(_color, key, StringComparison.Ordinal))
                {
                    return;
                }

                _color = key;
                BuildColorSwatches();
                MarkDirty();
            };

            ColorPanel.Children.Add(swatch);
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        UpdateStatus("编辑中…");
    }

    private async void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_saving)
        {
            _pendingSave = true;
            return;
        }

        _saving = true;
        UpdateStatus("正在保存…");
        try
        {
            _note = await _notes.SaveAsync(BuildNote());
            _dirty = false;
            UpdateStatus($"已保存 {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception exception)
        {
            _dirty = true;
            UpdateStatus($"保存失败：{exception.Message}", danger: true);
        }
        finally
        {
            _saving = false;
        }

        if (_pendingSave)
        {
            _pendingSave = false;
            await SaveAsync();
            return;
        }

        if (_closeRequested && !_closeAfterSave)
        {
            _closeRequested = false;
            if (!_dirty)
            {
                _closeAfterSave = true;
                Close();
            }
            else
            {
                await SaveBeforeCloseAsync();
            }
        }
    }

    /// <summary>关闭前异步冲刷：保存成功后再真正关闭，避免 UI 线程同步等待死锁。</summary>
    private async Task SaveBeforeCloseAsync()
    {
        _saving = true;
        UpdateStatus("正在保存…");
        try
        {
            _note = await _notes.SaveAsync(BuildNote());
            _dirty = false;
            _closeAfterSave = true;
            Close();
        }
        catch (Exception exception)
        {
            _dirty = true;
            UpdateStatus($"保存失败：{exception.Message}", danger: true);

            var result = MessageBox.Show(
                this,
                $"保存失败：{exception.Message}\n仍要关闭吗？未保存的修改会丢失。",
                "保存失败",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result == MessageBoxResult.Yes)
            {
                _closeAfterSave = true;
                Close();
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private NoteItem BuildNote() => _note with
    {
        Title = TitleBox.Text,
        Content = ContentBox.Text,
        Color = _color,
    };

    private void OnToChecklist(object sender, RoutedEventArgs e)
    {
        ApplyContent(ChecklistText.ToChecklist(ContentBox.Text));
    }

    private void OnToPlainText(object sender, RoutedEventArgs e)
    {
        ApplyContent(ChecklistText.ToPlainText(ContentBox.Text));
    }

    /// <summary>转为待办：先落库保证有 Id，再调用待办服务；同一条便签重复点击不会重复创建。</summary>
    private async void OnConvertToTodo(object sender, RoutedEventArgs e)
    {
        if (_dirty || _note.Id == 0)
        {
            await SaveAsync();
        }

        if (_note.Id == 0)
        {
            UpdateStatus("请先输入内容再转为待办", danger: true);
            return;
        }

        try
        {
            var already = (await _todos.ListAsync(cancellationToken: CancellationToken.None))
                .Any(todo => todo.NoteId == _note.Id);
            var todo = await _todos.ConvertFromNoteAsync(_note.Id);
            UpdateStatus(already ? $"已在待办中：{todo.Text}" : $"已转为待办：{todo.Text}");
        }
        catch (Exception exception)
        {
            UpdateStatus($"转为待办失败：{exception.Message}", danger: true);
        }
    }

    private void ApplyContent(string text)
    {
        if (string.Equals(text, ContentBox.Text, StringComparison.Ordinal))
        {
            return;
        }

        ContentBox.Text = text;
        ContentBox.CaretIndex = text.Length;
        MarkDirty();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (PreviewHost is null || ContentBox is null)
        {
            return;
        }

        var preview = PreviewTab?.IsChecked == true;
        if (preview)
        {
            PreviewHost.Child = NotePreviewRenderer.Build(
                NoteMarkdown.Parse(ContentBox.Text),
                compact: false);
        }

        PreviewScroll.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        ContentBox.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _saveTimer.Stop();

        // 空白新便签且从未保存：不落库，直接关闭。
        var blank = _note.Id == 0
            && string.IsNullOrWhiteSpace(TitleBox.Text)
            && string.IsNullOrWhiteSpace(ContentBox.Text);
        if (blank || _closeAfterSave || !_dirty)
        {
            return;
        }

        // 先取消本次关闭，异步保存完成后再调用 Close()，避免 UI 线程死等。
        e.Cancel = true;

        if (_saving)
        {
            // 防抖保存正在进行：标记关闭意图，保存结束后统一收尾。
            _pendingSave = true;
            _closeRequested = true;
            return;
        }

        await SaveBeforeCloseAsync();
    }

    private void UpdateStatus(string text, bool danger = false)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(
            TextBlock.ForegroundProperty,
            danger ? "Theme.DangerBrush" : "Theme.TextTertiaryBrush");
    }
}
