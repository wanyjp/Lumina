using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.UI.Wpf.Calendar;

/// <summary>
/// P1-05 ~ P1-08 月历：42 格、农历/节气、今天标记、休/班角标、
/// 日期选中、跨天色条、N+ 密度角标与横/竖版行高自适应。
/// </summary>
public partial class CalendarMonthView : UserControl
{
    private static readonly string[] MondayFirstHeaders = ["一", "二", "三", "四", "五", "六", "日"];
    private static readonly string[] SundayFirstHeaders = ["日", "一", "二", "三", "四", "五", "六"];

    private readonly ICalendarService _calendar;
    private readonly ISettingsStore _settings;
    private readonly CnHolidayCalendar _holidays = CnHolidayCalendar.Default;

    private IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduleEvent>> _dayEvents =
        new Dictionary<DateOnly, IReadOnlyList<ScheduleEvent>>();
    private IReadOnlyDictionary<DateOnly, IReadOnlyList<TodoItem>> _dayTodos =
        new Dictionary<DateOnly, IReadOnlyList<TodoItem>>();
    private DateOnly _selectedDate;
    private int _year;
    private int _month;
    private bool _vertical;
    private IDisposable? _settingsSubscription;

    public CalendarMonthView(ICalendarService calendar, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(settings);

        _calendar = calendar;
        _settings = settings;
        _vertical = string.Equals(
            settings.Get<string>(PreferenceKeys.CalendarLayout),
            "vertical",
            StringComparison.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(DateTime.Now);
        _year = today.Year;
        _month = today.Month;
        _selectedDate = today;

        InitializeComponent();
        Render();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settingsSubscription ??= _settings.Changed.Subscribe(
            new ActionObserver<SettingChanged>(OnSettingChanged));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
    }

    /// <summary>设置中心或抽屉修改布局/周起始后，月历立即重绘。</summary>
    private void OnSettingChanged(SettingChanged change)
    {
        if (change.Key == PreferenceKeys.CalendarLayout)
        {
            var vertical = string.Equals(
                _settings.Get<string>(PreferenceKeys.CalendarLayout, PreferenceDefaults.CalendarLayout),
                "vertical",
                StringComparison.OrdinalIgnoreCase);

            if (vertical != _vertical)
            {
                _vertical = vertical;
                Render();
                LayoutChanged?.Invoke(_vertical);
            }
        }
        else if (change.Key == PreferenceKeys.WeekStart)
        {
            Render();
        }
    }

    /// <summary>用户点击日期或翻月后触发，供右侧当日列表联动。</summary>
    public event Action<DateOnly>? DateSelected;

    /// <summary>显示月份变化（翻月、今天、点击补位日期）后触发。</summary>
    public event Action<int, int>? MonthChanged;

    /// <summary>横/竖版切换后触发；true 表示竖版。</summary>
    public event Action<bool>? LayoutChanged;

    public DateOnly SelectedDate => _selectedDate;

    /// <summary>注入按日分组的事项与待办，用于色条、N+、圆点与悬停清单。</summary>
    public void SetDayData(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduleEvent>> events,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<TodoItem>> todos)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(todos);

        _dayEvents = events;
        _dayTodos = todos;
        Render();
    }

    /// <summary>在当前显示月内更新选中日期；跨月请使用 ShowMonth。</summary>
    public void ShowSelectedDate(DateOnly date)
    {
        if (date.Year == _year && date.Month == _month)
        {
            _selectedDate = date;
            Render();
        }
    }

    /// <summary>切换到指定年月；保留当前选中日或落到该月 1 日。</summary>
    public void ShowMonth(int year, int month)
    {
        if (year is < 1901 or > 2100 || month is < 1 or > 12)
        {
            return;
        }

        _year = year;
        _month = month;

        var today = DateOnly.FromDateTime(DateTime.Now);
        _selectedDate = today.Year == year && today.Month == month
            ? today
            : new DateOnly(year, month, 1);

        Render();
        MonthChanged?.Invoke(_year, _month);
        DateSelected?.Invoke(_selectedDate);
    }

    private void Render()
    {
        var mondayFirst = _settings.Get<int>(PreferenceKeys.WeekStart) != 0;
        var model = _calendar.BuildMonth(_year, _month);

        MonthTitle.Text = $"{_year} 年 {_month} 月";

        BuildWeekdayHeader(mondayFirst);
        BuildDayGrid(model);

        HolidayNote.Text = BuildHolidayNote(_year);
    }

    /// <summary>
    /// 脚注只解释“休/班”角标与数据年份；具体文号按年份列出，
    /// 未收录年份明确说明降级口径，避免用户误以为数据缺失是缺陷。
    /// </summary>
    private string BuildHolidayNote(int year)
    {
        var note = $"法定节假日与调休数据：{CnHolidayCalendar.DescribeSources()}；红色“休”为放假，蓝色“班”为调休上班。";

        if (_holidays.HasData(year))
        {
            return note;
        }

        var supported = string.Join("、", CnHolidayCalendar.SupportedYears.Select(value => $"{value} 年"));
        return $"{year} 年暂无官方节假日数据（已收录 {supported}），法定工作日按周一至周五判定。";
    }

    private void BuildWeekdayHeader(bool mondayFirst)
    {
        WeekdayHeader.Children.Clear();
        var headers = mondayFirst ? MondayFirstHeaders : SundayFirstHeaders;

        foreach (var header in headers)
        {
            WeekdayHeader.Children.Add(new TextBlock
            {
                Text = header,
                Style = (Style)FindResource("Theme.Text.Caption"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    private void BuildDayGrid(CalendarMonthModel model)
    {
        var densities = model.Days
            .Select(day => CalendarDensityBuilder.Build(
                DayEvents(day.Date),
                DayTodos(day.Date),
                day.Date))
            .ToArray();

        DayGrid.Children.Clear();
        DayGrid.ColumnDefinitions.Clear();
        DayGrid.RowDefinitions.Clear();

        for (var column = 0; column < 7; column++)
        {
            DayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var baseHeight = _vertical
            ? CalendarDensityBuilder.VerticalBaseHeight
            : CalendarDensityBuilder.HorizontalBaseHeight;

        for (var row = 0; row < 6; row++)
        {
            var weekDensity = densities.Skip(row * 7).Take(7).ToArray();
            var maxLevels = weekDensity.Length == 0
                ? 0
                : weekDensity.Max(density => density.Bars.Count == 0
                    ? 0
                    : density.Bars.Max(bar => bar.Level) + 1);
            var hasMore = weekDensity.Any(density => density.HasMore);
            var height = baseHeight
                + maxLevels * CalendarDensityBuilder.BarLevelHeight
                + (hasMore ? CalendarDensityBuilder.MoreHeight : 0);

            DayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        }

        for (var index = 0; index < model.Days.Count; index++)
        {
            var cell = BuildDayCell(model.Days[index], densities[index]);
            Grid.SetRow(cell, index / 7);
            Grid.SetColumn(cell, index % 7);
            DayGrid.Children.Add(cell);
        }
    }

    private IReadOnlyList<ScheduleEvent> DayEvents(DateOnly date) =>
        _dayEvents.TryGetValue(date, out var events) ? events : Array.Empty<ScheduleEvent>();

    private IReadOnlyList<TodoItem> DayTodos(DateOnly date) =>
        _dayTodos.TryGetValue(date, out var todos) ? todos : Array.Empty<TodoItem>();

    private Border BuildDayCell(CalendarDayModel day, CalendarDayDensity density)
    {
        var isHoliday = _holidays.IsLegalHoliday(day.Date);
        var isMakeup = _holidays.IsMakeupWorkday(day.Date);
        var isSelected = _selectedDate == day.Date;
        var textBrush = isHoliday
            ? (Brush)FindResource("Theme.DangerBrush")
            : day.IsCurrentMonth
                ? (Brush)FindResource("Theme.TextBrush")
                : (Brush)FindResource("Theme.TextTertiaryBrush");

        var cell = new Border
        {
            Background = day.IsToday || isSelected
                ? (Brush)FindResource("Theme.AccentSoftBrush")
                : (Brush)FindResource("Theme.CardBrush"),
            BorderBrush = isSelected
                ? (Brush)FindResource("Theme.AccentBrush")
                : Brushes.Transparent,
            BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0),
            CornerRadius = (CornerRadius)FindResource("Theme.Radius.Small"),
            Margin = new Thickness(1),
            Padding = new Thickness(2, 3, 2, 2),
            SnapsToDevicePixels = true,
            Tag = day.Date,
            Cursor = Cursors.Hand,
        };
        cell.MouseLeftButtonDown += OnDayClick;

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        content.Children.Add(new TextBlock
        {
            Text = day.Date.Day.ToString(CultureInfo.InvariantCulture),
            FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono"),
            FontSize = 13,
            FontWeight = day.IsToday || isSelected ? FontWeights.Bold : FontWeights.Normal,
            Foreground = day.IsToday || isSelected
                ? (Brush)FindResource("Theme.AccentBrush")
                : textBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        if (density.Bars.Count > 0)
        {
            var bars = new StackPanel { Margin = new Thickness(0, 1, 0, 0) };

            foreach (var bar in density.Bars)
            {
                bars.Children.Add(new Border
                {
                    Height = 3,
                    Margin = new Thickness(0, 1, 0, 0),
                    Background = bar.Priority switch
                    {
                        Priority.High => (Brush)FindResource("Theme.DangerBrush"),
                        Priority.Low => (Brush)FindResource("Theme.TextTertiaryBrush"),
                        _ => (Brush)FindResource("Theme.AccentBrush"),
                    },
                    CornerRadius = new CornerRadius(
                        bar.IsStart ? 2 : 0,
                        bar.IsEnd ? 2 : 0,
                        bar.IsEnd ? 2 : 0,
                        bar.IsStart ? 2 : 0),
                });
            }

            content.Children.Add(bars);
        }

        content.Children.Add(new TextBlock
        {
            Text = day.LunarText ?? string.Empty,
            FontSize = 10,
            Foreground = isHoliday
                ? (Brush)FindResource("Theme.DangerBrush")
                : (Brush)FindResource("Theme.TextTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (density.HasMore)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"{density.HiddenCount}+",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Theme.AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
            });
        }

        if (density.ItemCount > 0)
        {
            var dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            };

            var events = DayEvents(day.Date);
            for (var index = 0; index < Math.Min(density.ItemCount, CalendarDensityBuilder.MaxDots); index++)
            {
                var item = index < events.Count ? events[index] : null;
                dots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Margin = new Thickness(1, 0, 1, 0),
                    Fill = item?.Priority switch
                    {
                        Priority.High => (Brush)FindResource("Theme.DangerBrush"),
                        Priority.Low => (Brush)FindResource("Theme.TextTertiaryBrush"),
                        _ => (Brush)FindResource("Theme.AccentBrush"),
                    },
                });
            }

            content.Children.Add(dots);
        }

        var grid = new Grid();
        grid.Children.Add(content);

        if (isHoliday || isMakeup)
        {
            var badge = new Border
            {
                Background = isHoliday
                    ? (Brush)FindResource("Theme.DangerBrush")
                    : (Brush)FindResource("Theme.AccentBrush"),
                CornerRadius = (CornerRadius)FindResource("Theme.Radius.Pill"),
                Padding = new Thickness(3, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 1, 0),
            };
            badge.Child = new TextBlock
            {
                Text = isHoliday ? "休" : "班",
                FontSize = 8.5,
                Foreground = isHoliday
                    ? (Brush)FindResource("Theme.OnDangerBrush")
                    : (Brush)FindResource("Theme.OnAccentBrush"),
            };
            grid.Children.Add(badge);
        }

        cell.Child = grid;

        if (density.ItemCount > 0)
        {
            cell.ToolTip = BuildTooltip(day.Date, DayEvents(day.Date), DayTodos(day.Date));
            ToolTipService.SetInitialShowDelay(cell, 400);
            ToolTipService.SetShowDuration(cell, 8000);
        }

        return cell;
    }

    private static string BuildTooltip(
        DateOnly date,
        IReadOnlyList<ScheduleEvent> events,
        IReadOnlyList<TodoItem> todos)
    {
        var lines = new List<string> { $"{date.Month} 月 {date.Day} 日" };

        foreach (var item in events)
        {
            var time = item.StartTime is { } start ? start.ToString("HH:mm") : "全天";
            lines.Add($"• {time}  {item.Title}");
        }

        foreach (var item in todos.Where(todo => todo.DueDate == date))
        {
            lines.Add($"• 待办  {item.Text}{(item.Done ? "（已完成）" : string.Empty)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void OnDayClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DateOnly date })
        {
            return;
        }

        var monthChanged = date.Year != _year || date.Month != _month;
        if (monthChanged)
        {
            _year = date.Year;
            _month = date.Month;
        }

        _selectedDate = date;
        Render();

        if (monthChanged)
        {
            MonthChanged?.Invoke(_year, _month);
        }

        DateSelected?.Invoke(date);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) => ChangeMonth(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => ChangeMonth(1);

    private void ChangeMonth(int offset)
    {
        var target = new DateOnly(_year, _month, 1).AddMonths(offset);
        _year = target.Year;
        _month = target.Month;

        var today = DateOnly.FromDateTime(DateTime.Now);
        _selectedDate = today.Year == _year && today.Month == _month ? today : target;

        Render();
        MonthChanged?.Invoke(_year, _month);
        DateSelected?.Invoke(_selectedDate);
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var monthChanged = today.Year != _year || today.Month != _month;

        _year = today.Year;
        _month = today.Month;
        _selectedDate = today;
        Render();

        if (monthChanged)
        {
            MonthChanged?.Invoke(_year, _month);
        }

        DateSelected?.Invoke(today);
    }
}
