using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Lumina.UI.Wpf.Controls;

namespace Lumina.Widgets;

/// <summary>
/// 今日日程与待办小组件：展示当天全部日程与未完成待办（超出高度时滚动），
/// 支持待办勾选完成与快速新增，其余编辑仍回到主界面。
/// </summary>
public partial class ScheduleWidgetView : UserControl, IWidgetGlassView
{
    private static readonly string[] Weekdays =
    [
        "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六",
    ];

    private readonly ICalendarService _calendar;
    private readonly IScheduleService _schedules;
    private readonly ITodoService _todos;
    private readonly DispatcherTimer _refresh;

    private bool _loading;
    private bool _subscribed;

    public ScheduleWidgetView(
        ICalendarService calendar,
        IScheduleService schedules,
        ITodoService todos)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(todos);

        _calendar = calendar;
        _schedules = schedules;
        _todos = todos;

        InitializeComponent();
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refresh.Tick += (_, _) => _ = RefreshAsync();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler? DragRequested;

    public event EventHandler? CloseRequested;

    public Border CardElement => Card;

    public WidgetGlassSurface GlassSurface => Glass;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _todos.Changed += OnTodosChanged;
            _schedules.Changed += OnSchedulesChanged;
            _subscribed = true;
        }

        _refresh.Start();
        _ = RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refresh.Stop();

        if (_subscribed)
        {
            _todos.Changed -= OnTodosChanged;
            _schedules.Changed -= OnSchedulesChanged;
            _subscribed = false;
        }
    }

    private void OnTodosChanged()
    {
        if (Dispatcher.CheckAccess())
        {
            _ = RefreshAsync();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
        }
    }

    private void OnSchedulesChanged() => OnTodosChanged();

    public async Task RefreshAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var events = await _calendar.EventsOnAsync(today, CancellationToken.None);
            var todos = await _todos.TodosBetweenAsync(today, today, CancellationToken.None);
            var pending = todos.Where(todo => !todo.Done).ToArray();

            DateText.Text = $"今日 · {today.Month} 月 {today.Day} 日 · {Weekdays[(int)today.DayOfWeek]}";
            SummaryText.Text = $"{events.Count} 条安排 · {pending.Length} 条待办";

            BuildEvents(events);
            BuildTodos(pending);
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildEvents(IReadOnlyList<ScheduleEvent> events)
    {
        EventsPanel.Children.Clear();

        if (events.Count == 0)
        {
            EventsPanel.Children.Add(HintText("今天没有日程。"));
            return;
        }

        foreach (var item in events)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = item.StartDate < DateOnly.FromDateTime(DateTime.Now)
                    ? "进行中"
                    : item.StartTime is { } start ? start.ToString("HH:mm") : "全天",
                FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono"),
                FontSize = 11,
                Foreground = (Brush)FindResource("Theme.AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var title = new TextBlock
            {
                Text = item.Title,
                FontSize = (double)FindResource("Theme.FontSize.Small"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (item.Done)
            {
                title.TextDecorations = TextDecorations.Strikethrough;
                title.Foreground = (Brush)FindResource("Theme.TextTertiaryBrush");
            }

            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var priority = BuildPriorityChip(item.Priority);
            Grid.SetColumn(priority, 2);
            grid.Children.Add(priority);

            if (item.Location.Length > 0)
            {
                var location = new TextBlock
                {
                    Text = $"· {item.Location}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("Theme.TextTertiaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 110,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(location, 3);
                grid.Children.Add(location);
            }

            EventsPanel.Children.Add(grid);
        }
    }

    private void BuildTodos(IReadOnlyList<TodoItem> pending)
    {
        TodosPanel.Children.Clear();

        if (pending.Count == 0)
        {
            TodosPanel.Children.Add(HintText("今天没有未完成待办。"));
            return;
        }

        foreach (var item in pending)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new CompletionGlyph
            {
                Style = (Style)FindResource("Theme.Completion"),
                CompletionStyle = CompletionStyle.Stroke,
                Done = false,
            };
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Theme.CompletionCheck"),
                Content = glyph,
                Tag = item.Id,
                IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "标记为已完成",
            };
            toggle.Checked += OnTodoToggle;
            toggle.Unchecked += OnTodoToggle;
            row.Children.Add(toggle);

            var title = new TextBlock
            {
                Text = item.Text,
                FontSize = (double)FindResource("Theme.FontSize.Small"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);

            TodosPanel.Children.Add(row);
        }
    }

    private Border BuildPriorityChip(Priority priority)
    {
        var (label, brushKey) = priority switch
        {
            Priority.High => ("高", "Theme.DangerBrush"),
            Priority.Low => ("低", "Theme.TextTertiaryBrush"),
            _ => ("中", "Theme.AccentBrush"),
        };

        return new Border
        {
            Style = (Style)FindResource("Theme.Chip"),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = (Brush)FindResource(brushKey),
            },
        };
    }

    private TextBlock HintText(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("Theme.Text.Tertiary"),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private async void OnTodoToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: long id })
        {
            return;
        }

        try
        {
            await _todos.ToggleDoneAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"更新待办失败：{ex.Message}";
        }
    }

    private async void OnQuickAddClick(object sender, RoutedEventArgs e) => await AddQuickTodoAsync();

    private async void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await AddQuickTodoAsync();
    }

    private async Task AddQuickTodoAsync()
    {
        var text = QuickAddBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var item = new TodoItem
        {
            Text = text,
            DueDate = DateOnly.FromDateTime(now.LocalDateTime),
            Priority = Priority.Normal,
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            await _todos.AddAsync(item, CancellationToken.None);
            QuickAddBox.Clear();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"添加失败：{ex.Message}";
        }
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

}
