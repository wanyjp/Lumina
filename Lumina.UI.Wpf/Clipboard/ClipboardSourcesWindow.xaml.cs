using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>展示一条剪贴板记录合并过的全部来源窗口（最新在前）。</summary>
public partial class ClipboardSourcesWindow : Window
{
    public ClipboardSourcesWindow(IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        InitializeComponent();
        Build(sources);
    }

    private void Build(IReadOnlyList<string> sources)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            var row = new Grid { Margin = new Thickness(8, 5, 8, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "Theme.AccentBrush");
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var text = new TextBlock
            {
                Text = index == 0 ? $"{sources[index]}（最近）" : sources[index],
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            SourceList.Children.Add(row);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
