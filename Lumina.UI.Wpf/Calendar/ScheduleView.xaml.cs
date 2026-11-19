using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Todos;
using Lumina.UI.Wpf.Controls;
using Lumina.UI.Wpf.Themes;
using Lumina.UI.Wpf.Todos;

namespace Lumina.UI.Wpf.Calendar;

/// <summary>
/// P1-06 日历页：左侧月历、右侧当日安排时间轴；提供新建、编辑、删除与跨天展示。
/// 周期规则与提醒触发在 P1-07 / P1-12 接入。
/// </summary>
public partial class ScheduleView : UserControl
{
    /// <summary>右侧“当日安排”列宽；横向布局下月历宽度按此预留。</summary>
    private const double DayPanelColumnWidth = 360;

    /// <summary>月历与当日面板之间的间距。</summary>
    private const double DayPanelGap = 16;

    private static readonly string[] WeekdayNames =
    [
        "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六",
    ];

    private readonly ICalendarService _calendar;
    private readonly IScheduleService _schedules;
    private readonly ITodoService _todos;
    private readonly CompletionStyleHub _styles;
    private readonly ThemeManager _themes;
    private readonly ISettingsStore _settings;
    private readonly CalendarMonthView _monthView;

    private int _loadVersion;
    private bool _initialized;
    private bool _verticalLayout;
    private bool _todoSubscribed;
    private bool _styleSubscribed;
    private bool _themeSubscribed;
    private bool _deferReload;
    private DispatcherTimer? _toggleReloadTimer;

    public ScheduleView(
        ICalendarService calendar,
        IScheduleService schedules,
        ITodoService todos,
        CompletionStyleHub styles,
        ThemeManager themes,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(settings);

        _calendar = calendar;
        _schedules = schedules;
        _todos = todos;
        _styles = styles;
        _themes = themes;
        _settings = settings;

        InitializeComponent();

        _monthView = new CalendarMonthView(calendar, settings);
        _monthView.DateSelected += OnDateSelected;
        _monthView.MonthChanged += OnMonthChanged;
        _monthView.LayoutChanged += OnLayoutChanged;
        CalendarHost.Content = _monthView;

        ApplyLayout(IsVerticalLayout());
        LayoutGrid.SizeChanged += OnLayoutGridSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _toggleReloadTimer?.Stop();

        if (_todoSubscribed)
        {
            _todos.Changed -= OnTodosChanged;
            _todoSubscribed = false;
        }

        if (_styleSubscribed)
        {
            _styles.Changed -= OnCompletionStyleChanged;
            _styles.MotionChanged -= OnMotionChanged;
            _styleSubscribed = false;
        }

        if (_themeSubscribed)
        {
            _themes.Changed -= OnThemeChanged;
            _themeSubscribed = false;
        }
    }

    private bool IsVerticalLayout() =>
        string.Equals(
            _settings.Get<string>(PreferenceKeys.CalendarLayout),
            "vertical",
            StringComparison.OrdinalIgnoreCase);

    private void OnLayoutChanged(bool vertical) => ApplyLayout(vertical);

    private void ApplyLayout(bool vertical)
    {
        _verticalLayout = vertical;

        if (vertical)
        {
            LayoutGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            LayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
            LayoutGrid.RowDefinitions[1].Height = GridLength.Auto;

            Grid.SetRow(DayPanel, 1);
            Grid.SetColumn(DayPanel, 0);
            Grid.SetColumnSpan(DayPanel, 2);
            Grid.SetColumnSpan(CalendarHost, 2);

            DayPanel.Width = double.NaN;
            DayPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            DayPanel.Margin = new Thickness(0, DayPanelGap, 0, 0);
            UpdateCalendarWidth();
            return;
        }

        // 月历列按自身宽度（画面一半）自适应，右侧当日面板铺满剩余空间，
        // 最大化时把右边的空余区域一起用起来。
        LayoutGrid.ColumnDefinitions[0].Width = GridLength.Auto;
        LayoutGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        LayoutGrid.RowDefinitions[1].Height = new GridLength(0);

        Grid.SetRow(DayPanel, 0);
        Grid.SetColumn(DayPanel, 1);
        Grid.SetColumnSpan(DayPanel, 1);
        Grid.SetColumnSpan(CalendarHost, 1);

        DayPanel.Width = double.NaN;
        DayPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        DayPanel.Margin = new Thickness(DayPanelGap, 0, 0, 0);
        UpdateCalendarWidth();
    }

    private void OnLayoutGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCalendarWidth();

    /// <summary>
    /// 横向布局下把月历宽度限制为画面宽度的一半：最大化时不再把 42 格拉成超宽行；
    /// 常态窗口仍按原宽度铺满，右侧“当日安排”面板保持不动。
    /// </summary>
    private void UpdateCalendarWidth()
    {
        if (_verticalLayout)
        {
            CalendarHost.Width = double.NaN;
            CalendarHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            return;
        }

        var available = LayoutGrid.ActualWidth;
        if (available <= 0)
        {
            return;
        }

        var windowWidth = Window.GetWindow(this)?.ActualWidth ?? 0;
        var maxAllowed = available - DayPanelColumnWidth;
        var width = windowWidth > 0 ? Math.Min(windowWidth / 2, maxAllowed) : maxAllowed;

        if (width <= 0)
        {
            CalendarHost.Width = double.NaN;
            CalendarHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            return;
        }

        CalendarHost.Width = width;
        CalendarHost.HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>加载当前选中月份与当日列表；重复调用只在首次执行。</summary>
    public async Task RefreshAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        var selected = _monthView.SelectedDate;
        await LoadMonthAsync(selected.Year, selected.Month);
        await RefreshDayAsync(selected);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _deferReload = false;
        _toggleReloadTimer?.Stop();

        if (!_todoSubscribed)
        {
            _todos.Changed += OnTodosChanged;
            _todoSubscribed = true;
        }

        if (!_styleSubscribed)
        {
            _styles.Changed += OnCompletionStyleChanged;
            _styles.MotionChanged += OnMotionChanged;
            _styleSubscribed = true;
        }

        if (!_themeSubscribed)
        {
            _themes.Changed += OnThemeChanged;
            _themeSubscribed = true;
        }

        await RefreshAsync();
    }

    private void OnCompletionStyleChanged(CompletionStyle style) => _ = ReloadAsync();

    private void OnMotionChanged() => _ = ReloadAsync();

    /// <summary>系统“减少动画”随主题字典重建，需要重新构图标以尊重最新设置。</summary>
    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => _ = ReloadAsync();

    private void OnTodosChanged()
    {
        if (_deferReload)
        {
            return;
        }

        _ = ReloadAsync();
    }

    /// <summary>勾选后等 180ms 动效播完再重建，避免打断当天待办图标动画。</summary>
    private void DeferReloadForAnimation()
    {
        _deferReload = true;

        if (_toggleReloadTimer is null)
        {
            _toggleReloadTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _toggleReloadTimer.Tick += OnDeferredReloadTick;
        }

        _toggleReloadTimer.Stop();
        _toggleReloadTimer.Start();
    }

    private void OnDeferredReloadTick(object? sender, EventArgs e)
    {
        _toggleReloadTimer?.Stop();
        _deferReload = false;
        _ = ReloadAsync();
    }

    private async void OnMonthChanged(int year, int month)
    {
        await LoadMonthAsync(year, month);
    }

    private async void OnDateSelected(DateOnly date)
    {
        await RefreshDayAsync(date);
    }

    private async Task LoadMonthAsync(int year, int month)
    {
        var version = ++_loadVersion;
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var from = first.AddDays(-7);
        var to = last.AddDays(7);

        try
        {
            var events = await _calendar.EventsBetweenAsync(from, to);
            var todoItems = await _todos.TodosBetweenAsync(from, to, CancellationToken.None);

            if (version != _loadVersion)
            {
                return;
            }

            var map = new Dictionary<DateOnly, List<ScheduleEvent>>();

            foreach (var item in events)
            {
                var end = item.EndDate ?? item.StartDate;

                for (var date = item.StartDate; date <= end; date = date.AddDays(1))
                {
                    if (date < from || date > to)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(date, out var list))
                    {
                        list = [];
                        map[date] = list;
                    }

                    list.Add(item);
                }
            }

            var todoMap = todoItems
                .Where(todo => todo.DueDate is not null)
                .GroupBy(todo => todo.DueDate!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<TodoItem>)group.ToArray());

            _monthView.SetDayData(
                map.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ScheduleEvent>)pair.Value),
                todoMap);
        }
        catch (Exception ex)
        {
            ListHint.Text = $"加载月历日程失败：{ex.Message}";
        }
    }

    private async Task RefreshDayAsync(DateOnly date)
    {
        var isToday = date == DateOnly.FromDateTime(DateTime.Now);
        DayTitle.Text = $"{date.Month} 月 {date.Day} 日 · {WeekdayNames[(int)date.DayOfWeek]}";
        DaySubtitle.Text = isToday ? "今日安排" : "当日安排";

        try
        {
            var events = await _calendar.EventsOnAsync(date);
            var todos = await _todos.TodosBetweenAsync(date, date, CancellationToken.None);
            BuildDayList(events, date);
            BuildTodoDayList(todos, date);
            ListHint.Text = events.Count == 0
                ? "点击“添加”创建这一天的第一条日程。"
                : $"共 {events.Count} 条安排；点击条目可编辑。";
        }
        catch (Exception ex)
        {
            DayList.Children.Clear();
            ListHint.Text = $"加载当日安排失败：{ex.Message}";
        }
    }

    private void BuildDayList(IReadOnlyList<ScheduleEvent> events, DateOnly date)
    {
        DayList.Children.Clear();

        if (events.Count == 0)
        {
            DayList.Children.Add(new TextBlock
            {
                Text = "这一天还没有安排。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                Margin = new Thickness(2, 12, 0, 0),
            });
            return;
        }

        foreach (var item in events)
        {
            DayList.Children.Add(BuildEventCard(item, date));
        }
    }

    private Border BuildEventCard(ScheduleEvent item, DateOnly date)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("Theme.CardBrush"),
            BorderBrush = (Brush)FindResource("Theme.LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("Theme.Radius.Small"),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var timeText = new TextBlock
        {
            Text = item.StartDate < date
                ? "进行中"
                : item.StartTime is { } start
                    ? start.ToString("HH:mm")
                    : "全天",
            FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Theme.AccentBrush"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        grid.Children.Add(timeText);

        var detail = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(detail, 1);

        var title = new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (item.Done)
        {
            title.TextDecorations = TextDecorations.Strikethrough;
            title.Foreground = (Brush)FindResource("Theme.TextTertiaryBrush");
        }

        detail.Children.Add(title);

        var tags = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        tags.Children.Add(BuildPriorityTag(item.Priority));

        if (item.Repeat is not null)
        {
            tags.Children.Add(new TextBlock
            {
                Text = "重复系列",
                Style = (Style)FindResource("Theme.Text.Caption"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        if (item.EndDate is { } end && end != item.StartDate)
        {
            tags.Children.Add(new TextBlock
            {
                Text = $"跨天 {item.StartDate:MM/dd}–{end:MM/dd}",
                Style = (Style)FindResource("Theme.Text.Caption"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        if (item.Reminder != ReminderOffset.None)
        {
            tags.Children.Add(new TextBlock
            {
                Text = $"提醒：{ReminderText(item.Reminder)}",
                Style = (Style)FindResource("Theme.Text.Caption"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        detail.Children.Add(tags);

        var meta = new List<string>();

        if (item.StartTime is not null && item.EndTime is { } finish)
        {
            meta.Add($"至 {finish:HH:mm}");
        }

        if (item.Location.Length > 0)
        {
            meta.Add(item.Location);
        }

        if (meta.Count > 0)
        {
            detail.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", meta),
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        grid.Children.Add(detail);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(actions, 2);

        var edit = new Button
        {
            Content = "编辑",
            Style = (Style)FindResource("Theme.Button.Ghost"),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12,
            Tag = item,
        };
        edit.Click += OnEditClick;
        actions.Children.Add(edit);

        var delete = new Button
        {
            Content = "删除",
            Style = (Style)FindResource("Theme.Button.Ghost"),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12,
            Foreground = (Brush)FindResource("Theme.DangerBrush"),
            Tag = item,
        };
        delete.Click += OnDeleteClick;
        actions.Children.Add(delete);

        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private Border BuildPriorityTag(Priority priority)
    {
        var text = priority switch
        {
            Priority.High => "高",
            Priority.Low => "低",
            _ => "中",
        };
        var foreground = priority switch
        {
            Priority.High => (Brush)FindResource("Theme.DangerBrush"),
            Priority.Low => (Brush)FindResource("Theme.TextTertiaryBrush"),
            _ => (Brush)FindResource("Theme.AccentBrush"),
        };

        var tag = new Border
        {
            Style = (Style)FindResource("Theme.Chip"),
            Padding = new Thickness(7, 1, 7, 1),
        };
        tag.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = foreground,
        };
        return tag;
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ScheduleEditorWindow(
            existing: null,
            defaultDate: _monthView.SelectedDate,
            defaultReminder: DefaultReminder())
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } item)
        {
            return;
        }

        try
        {
            await _schedules.AddAsync(item, CancellationToken.None);
            await ReloadAsync();
            ListHint.Text = $"已添加「{item.Title}」。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"保存日程失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ScheduleEvent item })
        {
            return;
        }

        var dialog = new ScheduleEditorWindow(
            existing: item,
            defaultDate: item.StartDate,
            defaultReminder: DefaultReminder())
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } updated)
        {
            return;
        }

        try
        {
            await _schedules.UpdateAsync(updated, CancellationToken.None);
            await ReloadAsync();
            ListHint.Text = $"已更新「{updated.Title}」。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"保存日程失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ScheduleEvent item })
        {
            return;
        }

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            item.Repeat is null
                ? $"删除日程「{item.Title}」？"
                : $"删除周期日程「{item.Title}」的整个系列？",
            "拾光",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _schedules.DeleteAsync([item.Id], CancellationToken.None);
            await ReloadAsync();
            ListHint.Text = "已删除所选日程。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"删除日程失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BuildTodoDayList(IReadOnlyList<TodoItem> todos, DateOnly date)
    {
        TodoDayList.Children.Clear();

        if (todos.Count == 0)
        {
            TodoDayList.Children.Add(new TextBlock
            {
                Text = "这一天没有待办。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                Margin = new Thickness(2, 6, 0, 0),
            });
            TodoDayHint.Text = "点击＋新建当天待办。";
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var item in todos
                     .OrderBy(todo => todo.Done)
                     .ThenBy(todo => todo.DueTime ?? TimeOnly.MinValue))
        {
            TodoDayList.Children.Add(BuildTodoDayCard(item, today));
        }

        TodoDayHint.Text = $"共 {todos.Count} 条待办；完成状态会同步到“待办”页。";
    }

    private Border BuildTodoDayCard(TodoItem item, DateOnly today)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("Theme.CardBrush"),
            BorderBrush = (Brush)FindResource("Theme.LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("Theme.Radius.Small"),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new CompletionGlyph
        {
            Style = (Style)FindResource("Theme.Completion"),
            CompletionStyle = _styles.Current,
            Done = item.Done,
            ReducedMotion = _styles.ReducedMotion || _themes.IsReducedMotion,
        };

        var toggle = new CheckBox
        {
            Style = (Style)FindResource("Theme.CompletionCheck"),
            Content = glyph,
            Tag = item.Id,
            IsChecked = item.Done,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = item.Done ? "标记为未完成" : "标记为已完成",
        };
        toggle.Checked += OnTodoDayToggle;
        toggle.Unchecked += OnTodoDayToggle;
        grid.Children.Add(toggle);

        var detail = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        Grid.SetColumn(detail, 1);

        var title = new TextBlock
        {
            Text = item.Text,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (item.Done)
        {
            title.TextDecorations = TextDecorations.Strikethrough;
            title.Foreground = (Brush)FindResource("Theme.TextTertiaryBrush");
        }

        detail.Children.Add(title);
        detail.Children.Add(new TextBlock
        {
            Text = TodoDueFormatter.Format(item, today),
            Style = (Style)FindResource("Theme.Text.Caption"),
            Foreground = !item.Done && item.DueDate is { } due && due < today
                ? (Brush)FindResource("Theme.DangerBrush")
                : (Brush)FindResource("Theme.TextTertiaryBrush"),
        });

        grid.Children.Add(detail);
        card.Child = grid;
        return card;
    }

    private async void OnTodoDayToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: long id } toggle)
        {
            return;
        }

        if (toggle.Content is CompletionGlyph glyph)
        {
            glyph.Done = toggle.IsChecked == true;
        }

        DeferReloadForAnimation();

        try
        {
            await _todos.ToggleDoneAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            TodoDayHint.Text = $"更新待办失败：{ex.Message}";
        }
    }

    private async void OnAddTodoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TodoEditorWindow(null, _monthView.SelectedDate)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } item)
        {
            return;
        }

        try
        {
            await _todos.AddAsync(item, CancellationToken.None);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"保存待办失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ReloadAsync()
    {
        var selected = _monthView.SelectedDate;
        await LoadMonthAsync(selected.Year, selected.Month);
        await RefreshDayAsync(selected);
    }

    private ReminderOffset DefaultReminder()
    {
        var key = _settings.Get<string>(PreferenceKeys.ScheduleReminder);
        return key switch
        {
            "ontime" => ReminderOffset.OnTime,
            "m5" => ReminderOffset.Minutes5,
            "m30" => ReminderOffset.Minutes30,
            "h1" => ReminderOffset.Hour1,
            "d1" => ReminderOffset.Day1,
            "none" => ReminderOffset.None,
            _ => ReminderOffset.Minutes15,
        };
    }

    private static string ReminderText(ReminderOffset offset) => offset switch
    {
        ReminderOffset.OnTime => "准时",
        ReminderOffset.Minutes5 => "提前 5 分钟",
        ReminderOffset.Minutes15 => "提前 15 分钟",
        ReminderOffset.Minutes30 => "提前 30 分钟",
        ReminderOffset.Hour1 => "提前 1 小时",
        ReminderOffset.Day1 => "提前 1 天",
        _ => "不提醒",
    };
}
