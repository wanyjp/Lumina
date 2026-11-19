using System.Windows;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// 剪贴板正文编辑窗口：只对文本 / 链接 / HTML 开放；保存后由服务重新判定类型。
/// 图片与文件条目的左滑「编辑」在视图层转去备注与标签。
/// </summary>
public partial class ClipboardContentEditorWindow : Window
{
    public ClipboardContentEditorWindow(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        InitializeComponent();

        ContentBox.Text = item.TextContent ?? string.Empty;
        KindHint.Text = item.Kind switch
        {
            ClipboardKind.Link => "当前为链接；改为普通文本后会重新归为文本。",
            ClipboardKind.Html => "当前为 HTML 原始片段；保存后仍保持 HTML 类型。",
            _ => "当前为文本；整段改为单个 http/https 链接后会自动归为链接。",
        };

        Loaded += (_, _) =>
        {
            ContentBox.Focus();
            ContentBox.CaretIndex = ContentBox.Text.Length;
        };
    }

    /// <summary>保存时的正文；服务端会 trim 并重算归一化文本与哈希。</summary>
    public string ContentText { get; private set; } = string.Empty;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var text = ContentBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorText.Text = "内容不能为空。";
            return;
        }

        ContentText = text;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
