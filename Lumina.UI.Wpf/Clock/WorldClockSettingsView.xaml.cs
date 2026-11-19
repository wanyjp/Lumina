using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Clock;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// P1-02 世界时钟地区管理：当前地区下拉选择、城市目录下拉添加与移除。
/// 目录覆盖全球首都/多时区代表城市，按首字母排序，支持输入搜索。
/// </summary>
public partial class WorldClockSettingsView : UserControl
{
    private readonly IWorldClockService _worldClockService;
    private bool _suppress;

    public WorldClockSettingsView(IWorldClockService worldClockService)
    {
        ArgumentNullException.ThrowIfNull(worldClockService);

        _worldClockService = worldClockService;
        InitializeComponent();
        _worldClockService.Changed += OnWorldClockChanged;
        Unloaded += OnUnloaded;
        Refresh();
    }

    private void OnWorldClockChanged()
    {
        Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _worldClockService.Changed -= OnWorldClockChanged;
    }

    private void Refresh()
    {
        _suppress = true;
        try
        {
            var rows = new List<RegionChoice> { new(0, "本地时间") };
            rows.AddRange(_worldClockService.Cities()
                .Select(city => new RegionChoice(city.Id, city.DisplayName)));

            RegionBox.ItemsSource = rows;
            RegionBox.DisplayMemberPath = nameof(RegionChoice.Label);
            RegionBox.SelectedItem = rows.FirstOrDefault(row => row.CityId == _worldClockService.SelectedCityId)
                ?? rows[0];

            var existingIana = _worldClockService.Cities()
                .Select(city => city.IanaId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var options = WorldCityCatalog.All
                .Where(option => !existingIana.Contains(option.IanaId))
                .ToList();

            CityBox.ItemsSource = options;
            CityBox.DisplayMemberPath = nameof(WorldCityOption.DisplayName);
            CityBox.SelectedIndex = options.Count > 0 ? 0 : -1;

            AddButton.IsEnabled = options.Count > 0;
            RemoveButton.IsEnabled = _worldClockService.SelectedCityId != 0;

            var selected = rows.FirstOrDefault(row => row.CityId == _worldClockService.SelectedCityId);
            RegionHint.Text = options.Count == 0
                ? "城市目录已全部添加。"
                : $"当前：{selected?.Label ?? "本地时间"}；已添加 {existingIana.Count} 个地区。";
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnRegionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || RegionBox.SelectedItem is not RegionChoice choice)
        {
            return;
        }

        _worldClockService.SelectCity(choice.CityId);
        RemoveButton.IsEnabled = choice.CityId != 0;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var option = ResolveOption();
        if (option is null)
        {
            RegionHint.Text = "请选择城市，或输入目录中的城市名称。";
            return;
        }

        try
        {
            _worldClockService.AddCity(option.IanaId, option.DisplayName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            RegionHint.Text = ex.Message;
            return;
        }

        Refresh();
    }

    private WorldCityOption? ResolveOption()
    {
        // 手动输入优先：可编辑下拉在输入未匹配时会保留上一次选中项。
        var text = CityBox.Text.Trim();
        if (text.Length > 0)
        {
            var match = WorldCityCatalog.All.FirstOrDefault(option =>
                string.Equals(option.DisplayName, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.IanaId, text, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return CityBox.SelectedItem as WorldCityOption;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        var cityId = _worldClockService.SelectedCityId;
        if (cityId == 0)
        {
            return;
        }

        _worldClockService.RemoveCity(cityId);
    }

    /// <summary>
    /// 当前地区下拉项。WPF 的 DisplayMemberPath 对私有嵌套类型不可靠，
    /// 这里同时重写 ToString 兜底，避免下拉显示成 “RegionChoice { ... }”。
    /// </summary>
    private sealed record RegionChoice(long CityId, string Label)
    {
        public override string ToString() => Label;
    }
}
