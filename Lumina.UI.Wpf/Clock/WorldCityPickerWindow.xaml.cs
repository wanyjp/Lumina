using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lumina.Domain.Models;
using Lumina.Services.Clock;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// 世界时钟城市选择窗：搜索目录、逐行添加、已添加地区移除。
/// 时钟页右上角「添加城市」打开本窗，选中结果即时写回并刷新卡片。
/// </summary>
public partial class WorldCityPickerWindow : Window
{
    private readonly IWorldClockService _worldClock;

    public WorldCityPickerWindow(IWorldClockService worldClock)
    {
        ArgumentNullException.ThrowIfNull(worldClock);

        _worldClock = worldClock;
        InitializeComponent();
        Rebuild();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Rebuild()
    {
        var query = SearchBox.Text.Trim();
        var existing = _worldClock.Cities();
        var existingIana = existing
            .Select(city => city.IanaId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CatalogPanel.Children.Clear();
        var options = WorldCityCatalog.All
            .Where(option => !existingIana.Contains(option.IanaId))
            .Where(option => query.Length == 0
                || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.IanaId.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var option in options)
        {
            CatalogPanel.Children.Add(BuildCatalogRow(option));
        }

        if (options.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = query.Length == 0 ? "城市目录已全部添加。" : "没有匹配的城市。",
                Margin = new Thickness(10),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
            CatalogPanel.Children.Add(hint);
        }

        AddedPanel.Children.Clear();
        foreach (var city in existing)
        {
            AddedPanel.Children.Add(BuildAddedRow(city));
        }

        if (existing.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "还没有添加地区，从上方目录选择即可。",
                Margin = new Thickness(10),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
            AddedPanel.Children.Add(hint);
        }
    }

    private UIElement BuildCatalogRow(WorldCityOption option)
    {
        var (offsetText, timeText) = Describe(option.IanaId);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = option.DisplayName,
            FontWeight = FontWeights.SemiBold,
        });

        var detail = new TextBlock
        {
            Text = string.IsNullOrEmpty(offsetText) ? timeText : $"{offsetText} · {timeText}",
            Margin = new Thickness(0, 2, 0, 0),
        };
        detail.SetResourceReference(TextBlock.FontSizeProperty, "Theme.FontSize.Caption");
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        textPanel.Children.Add(detail);

        var addButton = new Button
        {
            Content = "添加",
            Style = (Style)FindResource("Theme.Button.Secondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        AutomationProperties.SetName(addButton, $"添加 {option.DisplayName}");
        addButton.Click += (_, _) => AddCity(option);

        return BuildRow(textPanel, addButton);
    }

    private UIElement BuildAddedRow(WorldCity city)
    {
        var (offsetText, timeText) = Describe(city.IanaId);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = city.DisplayName,
            FontWeight = FontWeights.SemiBold,
        });

        var detail = new TextBlock
        {
            Text = string.IsNullOrEmpty(offsetText) ? timeText : $"{offsetText} · {timeText}",
            Margin = new Thickness(0, 2, 0, 0),
        };
        detail.SetResourceReference(TextBlock.FontSizeProperty, "Theme.FontSize.Caption");
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        textPanel.Children.Add(detail);

        var removeButton = new Button
        {
            Content = "移除",
            Style = (Style)FindResource("Theme.Button.Ghost"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        removeButton.SetResourceReference(ForegroundProperty, "Theme.DangerBrush");
        AutomationProperties.SetName(removeButton, $"移除 {city.DisplayName}");
        removeButton.Click += (_, _) => RemoveCity(city.Id);

        return BuildRow(textPanel, removeButton);
    }

    private static Grid BuildRow(UIElement content, UIElement action)
    {
        var grid = new Grid { Margin = new Thickness(6, 3, 6, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.SetValue(Grid.ColumnProperty, 0);
        action.SetValue(Grid.ColumnProperty, 1);
        grid.Children.Add(content);
        grid.Children.Add(action);
        return grid;
    }

    private void AddCity(WorldCityOption option)
    {
        try
        {
            _worldClock.AddCity(option.IanaId, option.DisplayName);
            StatusText.Text = $"已添加 {option.DisplayName}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            StatusText.Text = ex.Message;
        }

        Rebuild();
    }

    private void RemoveCity(long cityId)
    {
        try
        {
            _worldClock.RemoveCity(cityId);
            StatusText.Text = "已移除该地区";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"移除失败：{ex.Message}";
        }

        Rebuild();
    }

    private static (string Offset, string Time) Describe(string ianaId)
    {
        if (!WorldClockTimeZoneResolver.TryResolve(ianaId, out var zone) || zone is null)
        {
            return (string.Empty, "时区不可用");
        }

        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return (
            WorldClockTimeZoneResolver.DescribeOffset(zone, now),
            local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
