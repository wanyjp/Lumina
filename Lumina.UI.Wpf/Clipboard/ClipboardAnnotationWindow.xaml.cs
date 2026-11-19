using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// 备注与标签编辑对话框：标签按「最多 5 个、每个 12 字、忽略大小写去重」实时预览，
/// 保存时把原始输入交给服务再次标准化，界面与持久化口径一致。
/// </summary>
public partial class ClipboardAnnotationWindow : Window
{
    public ClipboardAnnotationWindow(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        InitializeComponent();

        PreviewText.Text = ClipboardPlainText.Preview(item, 120);
        NoteBox.Text = item.Note ?? string.Empty;
        TagsBox.Text = ClipboardTagPolicy.ToInput(item.Tags);

        UpdatePreview();
        Loaded += (_, _) => NoteBox.Focus();
    }

    /// <summary>保存时的备注原文（服务端会 trim 并丢弃空白）。</summary>
    public string NoteText { get; private set; } = string.Empty;

    /// <summary>保存时的标签原始输入；标准化由服务层负责。</summary>
    public string TagsText { get; private set; } = string.Empty;

    private void OnTagsChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        ChipPreview.Children.Clear();

        var tags = ClipboardTagPolicy.FromInput(TagsBox.Text, out var dropped);

        foreach (var tag in tags)
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

            ChipPreview.Children.Add(chip);
        }

        var parts = new List<string>
        {
            $"已用 {tags.Count}/{ClipboardTagPolicy.MaxCount} 个标签",
        };

        if (dropped > 0)
        {
            parts.Add($"{dropped} 个重复、空白或超限的标签会被忽略");
        }

        CounterText.Text = string.Join("；", parts) + "。";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        NoteText = NoteBox.Text ?? string.Empty;
        TagsText = TagsBox.Text ?? string.Empty;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
