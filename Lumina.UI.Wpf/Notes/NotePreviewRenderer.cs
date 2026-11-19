using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lumina.Services.Notes;

namespace Lumina.UI.Wpf.Notes;

/// <summary>
/// 把 <see cref="NoteMarkdown"/> 解析出的白名单块渲染成 WPF 元素。
/// 全部用 <see cref="TextBlock"/>/<see cref="Run"/> 输出，因此原始 HTML 永远只是文字，
/// 不会被解释成标记。颜色与字体走主题资源，切主题后仍能跟随。
/// </summary>
internal static class NotePreviewRenderer
{
    public static FrameworkElement Build(IReadOnlyList<MarkdownBlock> blocks, bool compact)
    {
        var panel = new StackPanel();
        if (blocks.Count == 0)
        {
            var empty = CreateText("空便签", compact ? 12 : 13, secondary: true);
            panel.Children.Add(empty);
            return panel;
        }

        foreach (var block in blocks)
        {
            panel.Children.Add(BuildBlock(block, compact));
        }

        return panel;
    }

    private static FrameworkElement BuildBlock(MarkdownBlock block, bool compact)
    {
        return block.Kind switch
        {
            MarkdownBlockKind.Bullet => BuildListRow("•", block.Inlines, compact, null),
            MarkdownBlockKind.Ordered => BuildListRow($"{block.Number}.", block.Inlines, compact, null),
            MarkdownBlockKind.Check => BuildListRow(
                block.Checked ? "☑" : "☐",
                block.Inlines,
                compact,
                block.Checked),
            MarkdownBlockKind.Code => BuildCode(block, compact),
            _ => BuildParagraph(block.Inlines, compact, 0),
        };
    }

    private static FrameworkElement BuildParagraph(
        IReadOnlyList<MarkdownInline> inlines,
        bool compact,
        double indent)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = compact ? 12 : 13,
            Margin = new Thickness(indent, 0, 0, compact ? 4 : 6),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextBrush");
        AppendInlines(text, inlines, compact);
        return text;
    }

    private static FrameworkElement BuildListRow(
        string glyph,
        IReadOnlyList<MarkdownInline> inlines,
        bool compact,
        bool? done)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, compact ? 3 : 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new TextBlock
        {
            Text = glyph,
            FontSize = compact ? 12 : 13,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        marker.SetResourceReference(
            TextBlock.ForegroundProperty,
            done == true ? "Theme.AccentBrush" : "Theme.TextTertiaryBrush");
        grid.Children.Add(marker);

        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = compact ? 12 : 13,
        };
        body.SetResourceReference(
            TextBlock.ForegroundProperty,
            done == true ? "Theme.TextSecondaryBrush" : "Theme.TextBrush");
        AppendInlines(body, inlines, compact);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        return grid;
    }

    private static FrameworkElement BuildCode(MarkdownBlock block, bool compact)
    {
        var text = new TextBlock
        {
            Text = block.Inlines.Count > 0 ? block.Inlines[0].Text : string.Empty,
            FontFamily = (FontFamily)Application.Current.FindResource("Theme.FontFamilyMono"),
            FontSize = compact ? 11 : 12,
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondaryBrush");

        var border = new Border
        {
            Padding = new Thickness(compact ? 8 : 10),
            Margin = new Thickness(0, 0, 0, compact ? 4 : 6),
            CornerRadius = (CornerRadius)Application.Current.FindResource("Theme.Radius.Small"),
            Child = text,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Theme.FieldBrush");
        return border;
    }

    private static void AppendInlines(
        TextBlock target,
        IReadOnlyList<MarkdownInline> inlines,
        bool compact)
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
                    run.FontSize = compact ? 11 : 12;
                    run.Foreground = (Brush)Application.Current.FindResource("Theme.AccentBrush");
                    break;
            }

            target.Inlines.Add(run);
        }
    }

    private static TextBlock CreateText(string text, double fontSize, bool secondary)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
        };
        block.SetResourceReference(
            TextBlock.ForegroundProperty,
            secondary ? "Theme.TextTertiaryBrush" : "Theme.TextBrush");
        return block;
    }
}
