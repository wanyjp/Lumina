using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Clock;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>P1-02 世界时钟卡片网格：只展示，不提供地区选择入口。</summary>
public partial class WorldClockView : UserControl
{
    private readonly IClockService _clockService;
    private readonly IWorldClockService _worldClockService;
    private bool _subscribed;

    public WorldClockView(IClockService clockService, IWorldClockService worldClockService)
    {
        ArgumentNullException.ThrowIfNull(clockService);
        ArgumentNullException.ThrowIfNull(worldClockService);

        _clockService = clockService;
        _worldClockService = worldClockService;

        InitializeComponent();
        CardsControl.ItemsSource = Cards;
        Rebuild();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<WorldClockCardViewModel> Cards { get; } = [];

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            return;
        }

        _clockService.TickSecond += OnTickSecond;
        _worldClockService.Changed += OnWorldClockChanged;
        _subscribed = true;
        UpdateTimes();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            return;
        }

        _clockService.TickSecond -= OnTickSecond;
        _worldClockService.Changed -= OnWorldClockChanged;
        _subscribed = false;
    }

    private void OnTickSecond()
    {
        Dispatcher.BeginInvoke(new Action(UpdateTimes));
    }

    private void OnWorldClockChanged()
    {
        Dispatcher.BeginInvoke(new Action(Rebuild));
    }

    private void OnAddCityClick(object sender, RoutedEventArgs e)
    {
        var picker = new WorldCityPickerWindow(_worldClockService)
        {
            Owner = Window.GetWindow(this),
        };
        picker.ShowDialog();
    }

    private void Rebuild()
    {
        Cards.Clear();
        foreach (var display in _worldClockService.AllDisplays())
        {
            Cards.Add(new WorldClockCardViewModel(
                display.CityId,
                display.CityName,
                FormatTime(display),
                FormatDate(display),
                display.CityId == _worldClockService.SelectedCityId));
        }
    }

    private void UpdateTimes()
    {
        var displays = _worldClockService.AllDisplays()
            .ToDictionary(display => display.CityId);

        foreach (var card in Cards)
        {
            if (!displays.TryGetValue(card.CityId, out var display))
            {
                continue;
            }

            card.TimeText = FormatTime(display);
            card.DateText = FormatDate(display);
            card.IsSelected = display.CityId == _worldClockService.SelectedCityId;
        }
    }

    private static string FormatTime(WorldClockDisplay display) =>
        display.Time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatDate(WorldClockDisplay display) =>
        $"{display.Date.Month} 月 {display.Date.Day} 日";
}

/// <summary>世界时钟卡视图模型；秒级只更新文本属性，不重建卡片。</summary>
public sealed class WorldClockCardViewModel : INotifyPropertyChanged
{
    private string _timeText;
    private string _dateText;
    private bool _isSelected;

    public WorldClockCardViewModel(
        long cityId,
        string cityName,
        string timeText,
        string dateText,
        bool isSelected)
    {
        CityId = cityId;
        CityName = cityName;
        _timeText = timeText;
        _dateText = dateText;
        _isSelected = isSelected;
    }

    public long CityId { get; }

    public string CityName { get; }

    public string TimeText
    {
        get => _timeText;
        set => SetField(ref _timeText, value);
    }

    public string DateText
    {
        get => _dateText;
        set => SetField(ref _dateText, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
