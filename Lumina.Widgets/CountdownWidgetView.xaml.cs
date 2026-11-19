using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Countdowns;
using Lumina.UI.Wpf.Countdown;

namespace Lumina.Widgets;

/// <summary>
/// 倒计时桌面组件（P3-04）：展示分类图标、重复规则、滚动后的下次发生日与剩余天数。
/// 与主列表共用 <see cref="ICountdownService"/>，不在组件内自行计算日期。
/// </summary>
public partial class CountdownWidgetView : UserControl, IWidgetGlassView
{
    private readonly ICountdownService _countdowns;
    private readonly DispatcherTimer _refresh;

    private long? _sourceId;
    private bool _subscribed;

    public CountdownWidgetView(ICountdownService countdowns, long? sourceId)
    {
        ArgumentNullException.ThrowIfNull(countdowns);

        _countdowns = countdowns;
        _sourceId = sourceId;

        InitializeComponent();
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refresh.Tick += (_, _) => Refresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler? DragRequested;

    public event EventHandler? CloseRequested;

    public Border CardElement => Card;

    public WidgetGlassSurface GlassSurface => Glass;

    public void ApplySource(long? sourceId)
    {
        _sourceId = sourceId;
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _countdowns.Changed += OnCountdownChanged;
            _subscribed = true;
        }

        _refresh.Start();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refresh.Stop();

        if (_subscribed)
        {
            _countdowns.Changed -= OnCountdownChanged;
            _subscribed = false;
        }
    }

    private void OnCountdownChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Refresh));
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        var items = _countdowns.All();
        var item = _sourceId is { } id
            ? items.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        item ??= items.FirstOrDefault();

        if (item is null)
        {
            ShowEmpty();
            return;
        }

        var snapshot = _countdowns.Evaluate(item);
        var invalid = snapshot.NextOccurrence is null;
        var brushKey = invalid
            ? "Theme.Urgency.ExpiredBrush"
            : CountdownVisuals.UrgencyBrushKey(snapshot.Level);

        CategoryIcon.Data = CountdownVisuals.CategoryGeometry(item.Category);
        CategoryText.Text = CountdownText.CategoryLabel(item.Category);
        NameText.Text = item.Name;
        NumberText.Text = CountdownText.Number(snapshot);
        NumberText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        NumberUnit.Visibility = invalid ? Visibility.Collapsed : Visibility.Visible;
        RepeatText.Text = CountdownText.RepeatSemantic(item);
        NextDateText.Text = snapshot.NextOccurrence is { } next
            ? (item.Repeat == CountdownRepeat.None ? CountdownText.DateText(next) : "下次 " + CountdownText.DateText(next))
            : "日期无效";
        RemainingText.Text = CountdownText.Remaining(snapshot);
        Progress.SetResourceReference(ProgressBar.ForegroundProperty, brushKey);
        Progress.Value = Math.Round(snapshot.ProgressRatio * 100);
    }

    private void ShowEmpty()
    {
        CategoryIcon.Data = CountdownVisuals.CategoryGeometry(CountdownCategory.Other);
        CategoryText.Text = "倒计时";
        NameText.Text = "暂无倒计时";
        NumberText.Text = "—";
        NumberText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        NumberUnit.Visibility = Visibility.Collapsed;
        RepeatText.Text = "在倒计时页新增日期后会同步到这里";
        NextDateText.Text = string.Empty;
        RemainingText.Text = string.Empty;
        Progress.Value = 0;
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

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

}
