using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Widgets;

/// <summary>时钟卡片内容：与主窗口共享同一 <see cref="IClockService"/>，并复用时钟制式设置。</summary>
public partial class ClockWidgetView : UserControl, IWidgetGlassView
{
    private readonly IClockService _clock;
    private readonly IWorldClockService _worldClock;
    private readonly IDisposable _settingsSubscription;

    private bool _use12Hour;
    private string _periodMode;

    public ClockWidgetView(
        IClockService clock,
        IWorldClockService worldClock,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(worldClock);
        ArgumentNullException.ThrowIfNull(settings);

        _clock = clock;
        _worldClock = worldClock;
        _use12Hour = string.Equals(
            settings.Get<string>(PreferenceKeys.ClockFormat, PreferenceDefaults.ClockFormat),
            "12",
            StringComparison.Ordinal);
        _periodMode = ClockTextFormatter.NormalizePeriodMode(
            settings.Get<string>(PreferenceKeys.PeriodMode, PreferenceDefaults.PeriodMode));

        InitializeComponent();
        _clock.TickSecond += OnTickSecond;
        _worldClock.Changed += OnWorldClockChanged;
        _settingsSubscription = settings.Changed.Subscribe(new SettingObserver(this));
        Unloaded += OnUnloaded;
        UpdateClock();
    }

    public event EventHandler? DragRequested;

    public event EventHandler? CloseRequested;

    public Border CardElement => Card;

    public WidgetGlassSurface GlassSurface => Glass;

    private void OnTickSecond()
    {
        // TickSecond 来自后台计时器线程；先切回 UI 线程再做任何 WPF 访问。
        Dispatcher.BeginInvoke(new Action(UpdateClock));
    }

    private void UpdateClock()
    {
        var selected = _worldClock.SelectedDisplay();
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
            ? ClockTextFormatter.FormatShortDate(selected.Time, selected.LunarText)
            : $"{ClockTextFormatter.FormatShortDate(selected.Time, selected.LunarText)} · {selected.CityName}";
    }

    private void OnWorldClockChanged()
    {
        Dispatcher.BeginInvoke(new Action(UpdateClock));
    }

    private void OnSettingChanged(SettingChanged change)
    {
        if (change.Key == PreferenceKeys.ClockFormat)
        {
            _use12Hour = string.Equals(change.Value as string, "12", StringComparison.Ordinal);
        }
        else if (change.Key == PreferenceKeys.PeriodMode)
        {
            _periodMode = ClockTextFormatter.NormalizePeriodMode(change.Value as string);
        }
        else
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(UpdateClock));
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (WidgetDragHelper.IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }


    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _clock.TickSecond -= OnTickSecond;
        _worldClock.Changed -= OnWorldClockChanged;
        _settingsSubscription.Dispose();
    }

    private sealed class SettingObserver : IObserver<SettingChanged>
    {
        private readonly ClockWidgetView _owner;

        public SettingObserver(ClockWidgetView owner)
        {
            _owner = owner;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(SettingChanged value)
        {
            _owner.OnSettingChanged(value);
        }
    }
}
