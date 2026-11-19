using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// P1-01 数字时钟视图：秒级刷新只更新文本；12/24 制与现代时段/十二时辰即时联动。
/// 世界时钟城市选择在 P1-02 接入。
/// </summary>
public partial class ClockView : UserControl
{
    private readonly IClockService _clockService;
    private readonly IWorldClockService _worldClockService;
    private readonly ISettingsStore _settings;

    private bool _use12Hour;
    private string _periodMode;
    private bool _subscribed;
    private IDisposable? _settingsSubscription;

    public ClockView(
        IClockService clockService,
        IWorldClockService worldClockService,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(clockService);
        ArgumentNullException.ThrowIfNull(worldClockService);
        ArgumentNullException.ThrowIfNull(settings);

        _clockService = clockService;
        _worldClockService = worldClockService;
        _settings = settings;

        InitializeComponent();

        var clockFormat = settings.Get<string>(PreferenceKeys.ClockFormat, PreferenceDefaults.ClockFormat);
        _use12Hour = string.Equals(clockFormat, "12", StringComparison.Ordinal);
        _periodMode = ClockTextFormatter.NormalizePeriodMode(
            settings.Get<string>(PreferenceKeys.PeriodMode, PreferenceDefaults.PeriodMode));

        UpdateDisplay();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settingsSubscription ??= _settings.Changed.Subscribe(
            new ActionObserver<SettingChanged>(OnSettingChanged));

        if (IsVisible)
        {
            AttachClock();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
        DetachClock();
    }

    /// <summary>设置中心或抽屉修改时钟制式后，当前页面立即同步。</summary>
    private void OnSettingChanged(SettingChanged change)
    {
        if (change.Key == PreferenceKeys.ClockFormat)
        {
            _use12Hour = string.Equals(
                _settings.Get<string>(PreferenceKeys.ClockFormat, PreferenceDefaults.ClockFormat),
                "12",
                StringComparison.Ordinal);
            UpdateDisplay();
        }
        else if (change.Key == PreferenceKeys.PeriodMode)
        {
            _periodMode = ClockTextFormatter.NormalizePeriodMode(
                _settings.Get<string>(PreferenceKeys.PeriodMode, PreferenceDefaults.PeriodMode));
            UpdateDisplay();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            AttachClock();
        }
        else
        {
            DetachClock();
        }
    }

    private void AttachClock()
    {
        if (_subscribed)
        {
            return;
        }

        _clockService.TickSecond += OnTickSecond;
        _worldClockService.Changed += OnWorldClockChanged;
        _subscribed = true;
        UpdateDisplay();
    }

    private void DetachClock()
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
        // TickSecond 来自后台计时器线程；先切回 UI 线程再更新文本。
        Dispatcher.BeginInvoke(new Action(UpdateDisplay));
    }

    private void OnWorldClockChanged()
    {
        Dispatcher.BeginInvoke(new Action(UpdateDisplay));
    }

    private void UpdateDisplay()
    {
        var selected = _worldClockService.SelectedDisplay();
        var snapshot = new ClockSnapshot(
            selected.Time.ToUniversalTime(),
            selected.Time,
            selected.Date,
            selected.LunarText,
            selected.IsLocal ? null : selected.CityName);
        var display = ClockTextFormatter.Format(snapshot, _use12Hour, _periodMode);

        TimeText.Text = display.TimeText;
        PeriodText.Text = display.PeriodText ?? string.Empty;
        PeriodText.Visibility = display.PeriodText is null ? Visibility.Collapsed : Visibility.Visible;
        DateText.Text = selected.IsLocal
            ? display.DateText
            : $"{display.DateText} · {selected.CityName}时间";
        LunarText.Text = display.LunarText ?? string.Empty;
        LunarText.Visibility = string.IsNullOrWhiteSpace(display.LunarText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
