using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Countdowns;
using Lumina.Services.Focus;
using Microsoft.Win32;

namespace Lumina.UI.Wpf.Focus;

/// <summary>
/// 专注统计视图（P3-02）：近 7 天 / 近 30 天 / 历史月报三档，
/// 单一 <see cref="PeriodNavigator"/> 状态驱动翻页器、标题、图表与导出。
/// </summary>
public partial class FocusStatsView : UserControl
{
    private readonly IFocusStatsService _stats;
    private readonly IDailyGoalService _goals;
    private readonly ICountdownService _countdowns;
    private readonly PeriodNavigator _navigator = new();
    private readonly Func<DateTimeOffset> _now = () => DateTimeOffset.Now;

    private CancellationTokenSource? _loadCts;
    private FocusStatsSnapshot? _snapshot;
    private bool _initialized;
    private bool _loading;
    private bool _countdownSubscribed;

    public FocusStatsView(
        IFocusStatsService stats,
        IDailyGoalService goals,
        ICountdownService countdowns)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(countdowns);

        _stats = stats;
        _goals = goals;
        _countdowns = countdowns;

        InitializeComponent();
        _initialized = true;

        Loaded += async (_, _) =>
        {
            SubscribeCountdown();
            await ReloadAsync();
        };
        Unloaded += (_, _) =>
        {
            _loadCts?.Cancel();
            UnsubscribeCountdown();
        };
    }

    private void SubscribeCountdown()
    {
        if (_countdownSubscribed)
        {
            return;
        }

        _countdowns.Changed += OnCountdownChanged;
        _countdownSubscribed = true;
    }

    private void UnsubscribeCountdown()
    {
        if (!_countdownSubscribed)
        {
            return;
        }

        _countdowns.Changed -= OnCountdownChanged;
        _countdownSubscribed = false;
    }

    private void OnCountdownChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnCountdownChanged()));
            return;
        }

        if (_snapshot is { } snapshot)
        {
            RenderDetail(snapshot);
        }
    }

    private async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        try
        {
            var snapshot = await _stats
                .LoadAsync(_navigator.Kind, _navigator.Offset, cts.Token)
                .ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            _snapshot = snapshot;
            Render(snapshot);
        }
        catch (OperationCanceledException)
        {
            // 翻页过快时取消上一次加载，属正常路径。
        }
        finally
        {
            _loading = false;
        }
    }

    private void Render(FocusStatsSnapshot snapshot)
    {
        var now = _now();
        RangeText.Text = _navigator.RangeText(now);
        RelativeText.Text = _navigator.Label(now);
        NextButton.IsEnabled = _navigator.CanGoNext;

        // 历史月报隐藏左右箭头，但保留中间的月份与区间文字（区间是单一状态，标题不能消失）。
        PagerRow.Visibility = Visibility.Visible;
        PrevButton.Visibility = _navigator.ShowsPager ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = _navigator.ShowsPager ? Visibility.Visible : Visibility.Collapsed;
        ArchiveRow.Visibility = _navigator.ShowsPager ? Visibility.Collapsed : Visibility.Visible;

        if (!_navigator.ShowsPager)
        {
            SyncArchiveMonth();
        }

        var empty = snapshot.IsEmpty;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SummaryGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        TrendCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        HeatmapCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CompareCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        RenderDetail(snapshot);

        if (empty)
        {
            ExportCsvButton.IsEnabled = false;
            ExportJsonButton.IsEnabled = false;
            return;
        }

        ExportCsvButton.IsEnabled = true;
        ExportJsonButton.IsEnabled = true;

        var current = snapshot.Current;
        FocusValueText.Text = FormatMinutes(current.FocusMinutes);
        PomodoroValueText.Text = $"{current.PomodoroCount} 个";
        TodoValueText.Text = current.TotalTodos == 0
            ? "暂无待办"
            : $"{(double)current.CompletedTodos / current.TotalTodos:P0}";
        TodoDetailText.Text = current.TotalTodos == 0
            ? "本周期没有待办"
            : $"{current.CompletedTodos} / {current.TotalTodos} 已完成";

        FocusCompareText.Text = FormatDelta(current.FocusMinutes, snapshot.Previous.FocusMinutes, "分钟");
        PomodoroCompareText.Text = FormatDelta(current.PomodoroCount, snapshot.Previous.PomodoroCount, "个番茄");

        RenderGoal(snapshot);
        RenderTrend(snapshot);
        RenderHeatmap(snapshot);
        RenderCompare(snapshot);
    }

    private void RenderGoal(FocusStatsSnapshot snapshot)
    {
        GoalInput.Text = snapshot.TodayTargetMinutes > 0
            ? snapshot.TodayTargetMinutes.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        if (snapshot.TodayTargetMinutes <= 0)
        {
            GoalValueText.Text = "未设定";
            GoalDetailText.Text = "输入分钟数并保存，今天开始对比";
            GoalProgress.Value = 0;
            return;
        }

        GoalValueText.Text = $"{snapshot.TodayFocusMinutes} / {snapshot.TodayTargetMinutes} 分钟";
        GoalProgress.Value = Math.Min(
            1d,
            (double)snapshot.TodayFocusMinutes / snapshot.TodayTargetMinutes);
        GoalDetailText.Text = snapshot.TodayFocusMinutes >= snapshot.TodayTargetMinutes
            ? "今日目标已达成"
            : $"还差 {snapshot.TodayTargetMinutes - snapshot.TodayFocusMinutes} 分钟";
    }

    private void RenderTrend(FocusStatsSnapshot snapshot)
    {
        TrendHost.Children.Clear();
        TrendHost.ColumnDefinitions.Clear();
        TrendHost.RowDefinitions.Clear();
        TrendTitle.Text = _navigator.Kind == PeriodKind.MonthlyArchive
            ? "本月每日专注时长"
            : "每日专注时长";

        var daily = snapshot.Daily;
        if (daily.Count == 0)
        {
            return;
        }

        var max = Math.Max(1, daily.Max(point => point.FocusMinutes));

        // 柱体与日期标签拆成上下两行：标签各占固定列宽并按跨度居中，
        // 避免 30 天视图里 5 个字符挤进 20px 的窄列后换行成「10-2 / 4」。
        var span = daily.Count <= 10 ? 1 : (int)Math.Ceiling(daily.Count / 8d);

        TrendHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        TrendHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bars = new Grid();
        var labels = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(bars, 0);
        Grid.SetRow(labels, 1);
        TrendHost.Children.Add(bars);
        TrendHost.Children.Add(labels);

        for (var index = 0; index < daily.Count; index++)
        {
            var column = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            bars.ColumnDefinitions.Add(column);
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width });
        }

        for (var index = 0; index < daily.Count; index++)
        {
            var point = daily[index];

            if (point.FocusMinutes > 0)
            {
                var bar = new Border
                {
                    Height = Math.Max(3, 120d * point.FocusMinutes / max),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(2, 0, 2, 0),
                    ToolTip = $"{point.Date:MM-dd} · {point.FocusMinutes} 分钟 · 番茄 {point.PomodoroCount}",
                };
                bar.SetResourceReference(Border.BackgroundProperty, "Theme.AccentBrush");

                if (point.TargetMinutes > 0)
                {
                    bar.Opacity = point.GoalMet ? 1 : 0.75;
                }

                Grid.SetColumn(bar, index);
                bars.Children.Add(bar);
            }

            var shouldLabel = index % span == 0;

            if (!shouldLabel)
            {
                continue;
            }

            var label = new TextBlock
            {
                Text = point.Date.ToString("MM-dd", CultureInfo.InvariantCulture),
                Style = (Style)FindResource("Theme.Text.Caption"),
                FontSize = 10,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 0, 1, 0),
            };

            Grid.SetColumn(label, index);
            Grid.SetColumnSpan(label, Math.Min(span, daily.Count - index));
            labels.Children.Add(label);
        }

        // 基线：让 0 分钟的空白日也有参照，而不是靠 2px 短柱冒充刻度。
        var axis = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (Brush)FindResource("Theme.LineBrush"),
        };
        Grid.SetColumnSpan(axis, Math.Max(1, daily.Count));
        bars.Children.Add(axis);
    }

    private void RenderHeatmap(FocusStatsSnapshot snapshot)
    {
        HeatmapHost.Children.Clear();

        var byDate = snapshot.Daily.ToDictionary(point => point.Date);
        var start = snapshot.Current.Start;
        var end = snapshot.Current.End;
        var firstMonday = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        var max = Math.Max(1, snapshot.Daily.Max(point => point.FocusMinutes));

        HeatmapHost.Children.Add(BuildWeekdayHeader());

        var date = firstMonday;
        while (date <= end)
        {
            var row = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            for (var day = 0; day < 7; day++)
            {
                var cellDate = date.AddDays(day);
                row.Children.Add(BuildHeatCell(cellDate, start, end, byDate, max));
            }

            HeatmapHost.Children.Add(row);
            date = date.AddDays(7);
        }
    }

    private FrameworkElement BuildWeekdayHeader()
    {
        var header = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" })
        {
            header.Children.Add(new TextBlock
            {
                Text = name,
                Style = (Style)FindResource("Theme.Text.Caption"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        return header;
    }

    private FrameworkElement BuildHeatCell(
        DateOnly date,
        DateOnly start,
        DateOnly end,
        IReadOnlyDictionary<DateOnly, FocusDailyPoint> byDate,
        int max)
    {
        if (date < start || date > end)
        {
            return new Border { Width = 30, Height = 30, Margin = new Thickness(3) };
        }

        byDate.TryGetValue(date, out var point);
        var minutes = point?.FocusMinutes ?? 0;
        var level = minutes == 0 ? 0 : Math.Clamp(1 + (minutes * 4 / max), 1, 4);
        var opacity = level switch
        {
            0 => 0d,
            1 => 0.22,
            2 => 0.42,
            3 => 0.65,
            _ => 1d,
        };

        var cell = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            ToolTip = point is null
                ? $"{date:MM-dd} · 无记录"
                : $"{date:MM-dd} · {minutes} 分钟 · 番茄 {point.PomodoroCount}" +
                  (point.TargetMinutes > 0
                      ? $" · 目标 {point.TargetMinutes}（{(point.GoalMet ? "达成" : "未达成")}）"
                      : " · 未设目标"),
        };

        if (minutes > 0)
        {
            cell.SetResourceReference(Border.BackgroundProperty, "Theme.AccentBrush");
            cell.Opacity = opacity;
        }
        else
        {
            cell.SetResourceReference(Border.BackgroundProperty, "Theme.AccentSoftBrush");
            cell.Opacity = 0.35;
        }

        if (point is { GoalMet: true })
        {
            cell.SetResourceReference(Border.BorderBrushProperty, "Theme.AccentBrush");
            cell.BorderThickness = new Thickness(1.5);
        }

        return cell;
    }

    private void RenderCompare(FocusStatsSnapshot snapshot)
    {
        var current = snapshot.Current;
        var previous = snapshot.Previous;
        var days = snapshot.Daily.Count;
        var avg = days == 0 ? 0 : current.FocusMinutes / (double)days;
        var goalDays = snapshot.Daily.Count(point => point.TargetMinutes > 0);
        var metDays = snapshot.Daily.Count(point => point.GoalMet);

        var delta = previous.FocusMinutes == 0
            ? "上期没有专注记录，暂无可比基线。"
            : $"专注时长较上期{(current.FocusMinutes >= previous.FocusMinutes ? "增加" : "减少")} " +
              $"{Math.Abs(current.FocusMinutes - previous.FocusMinutes)} 分钟" +
              $"（{Math.Abs((double)(current.FocusMinutes - previous.FocusMinutes) / previous.FocusMinutes):P0}）。";

        CompareText.Text =
            $"本期日均 {avg:0.#} 分钟；上期 {previous.FocusMinutes} 分钟、{previous.PomodoroCount} 个番茄。{delta}" +
            $"\n{(goalDays == 0 ? "本期没有设定每日目标。" : $"设定目标的 {goalDays} 天中达成 {metDays} 天。")}";
    }

    private void RenderDetail(FocusStatsSnapshot snapshot)
    {
        var today = DateOnly.FromDateTime(_now().LocalDateTime);
        var summary = CountdownSummaryBuilder.Build(_countdowns.All(), today);

        DetailFocusText.Text = snapshot.IsEmpty
            ? "本周期暂无专注记录。"
            : $"本期专注 {FormatMinutes(snapshot.Current.FocusMinutes)} · 番茄 {snapshot.Current.PomodoroCount} 个 · "
              + $"待办完成 {snapshot.Current.CompletedTodos} / {snapshot.Current.TotalTodos}。";

        var nearest = summary.Nearest is { } item && summary.NearestSnapshot is { } nearestSnapshot
            ? nearestSnapshot.DaysRemaining == 0
                ? $" · 最近「{item.Name}」就是今天"
                : $" · 最近「{item.Name}」还有 {nearestSnapshot.DaysRemaining} 天"
            : string.Empty;

        DetailCountdownText.Text =
            $"倒计时：共 {summary.Total} 项（{summary.CategoryText}） · 30 天内 {summary.DueWithin30Days} 项{nearest}";

        DetailCard.Visibility = snapshot.IsEmpty && summary.Total == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} 分钟";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} 小时" : $"{hours} 小时 {rest} 分";
    }

    private static string FormatDelta(int current, int previous, string unit)
    {
        if (previous == 0)
        {
            return current == 0 ? "—" : $"上期 0 {unit}";
        }

        var delta = (double)(current - previous) / previous;
        return delta == 0 ? $"与上期持平（{current} {unit}）" : $"{(delta > 0 ? "+" : string.Empty)}{delta:P0} 较上期";
    }

    /// <summary>归档月份：列出最近 12 个自然月，选中即切换偏移（不依赖下拉弹层，键盘可达）。</summary>
    private void SyncArchiveMonth()
    {
        var now = _now();
        var currentMonth = new DateOnly(now.Year, now.Month, 1);

        ArchiveMonthBar.Children.Clear();
        for (var back = 0; back < 12; back++)
        {
            var selected = back == -_navigator.Offset;
            var label = $"{currentMonth.AddMonths(-back):yyyy 年 M 月}" + (back == 0 ? "（本月）" : string.Empty);
            var button = new Button
            {
                Content = label,
                Tag = back,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 5, 10, 5),
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = selected ? "当前查看的归档月份" : "查看该月归档",
                Style = (Style)FindResource(selected ? "Theme.Button.Primary" : "Theme.Button.Ghost"),
            };

            button.Click += OnArchiveMonthClick;
            ArchiveMonthBar.Children.Add(button);
        }
    }

    private async void OnArchiveMonthClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int back })
        {
            return;
        }

        _navigator.SetOffset(-back);
        await ReloadAsync();
    }

    private async void OnPeriodChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var kind = tag switch
        {
            "month30" => PeriodKind.Month30,
            "archive" => PeriodKind.MonthlyArchive,
            _ => PeriodKind.Week7,
        };

        _navigator.Switch(kind);
        await ReloadAsync();
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        _navigator.MovePrevious();
        await ReloadAsync();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_navigator.MoveNext())
        {
            await ReloadAsync();
        }
    }

    private async void OnSaveGoalClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(GoalInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 0 or > FocusAggregator.MaxDailyGoalMinutes)
        {
            ExportStatusText.Text = "目标需为 0–1440 的整数";
            return;
        }

        await _goals.SetTargetMinutesAsync(DateOnly.FromDateTime(_now().LocalDateTime), minutes)
            .ConfigureAwait(true);
        ExportStatusText.Text = "目标已保存";
        await ReloadAsync();
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e) => Export("csv");

    private void OnExportJsonClick(object sender, RoutedEventArgs e) => Export("json");

    private void Export(string format)
    {
        if (_snapshot is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出专注统计",
            Filter = format == "csv" ? "CSV 文件 (*.csv)|*.csv" : "JSON 文件 (*.json)|*.json",
            FileName = $"lumina-focus-{_now():yyyyMMdd}{(_navigator.Kind == PeriodKind.MonthlyArchive ? "-month" : string.Empty)}.{format}",
            AddExtension = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var path = format == "csv"
                ? FocusStatsExporter.WriteCsv(dialog.FileName, _snapshot)
                : FocusStatsExporter.WriteJson(dialog.FileName, _snapshot);

            ExportStatusText.Text = "已导出 " + Path.GetFileName(path);
            ExportStatusText.ToolTip = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"导出失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
