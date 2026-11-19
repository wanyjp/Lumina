using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Completion;
using Lumina.Services.Contracts;
using Lumina.Services.Todos;
using Lumina.UI.Wpf.Controls;
using Lumina.UI.Wpf.Themes;

namespace Lumina.UI.Wpf.Todos;

/// <summary>
/// P1-09 待办页：新建/编辑/完成/删除、三组筛选、四种排序、逾期提示与完成率。
/// 默认按智能排序，筛选控件窄屏自动换行。
/// </summary>
public partial class TodoView : UserControl
{
    private static readonly (string Key, string Label)[] StatusOptions =
    [
        ("all", "全部状态"),
        ("active", "未完成"),
        ("completed", "已完成"),
        ("overdue", "已逾期"),
    ];

    private static readonly (string Key, string Label)[] PriorityOptions =
    [
        ("all", "全部优先级"),
        ("high", "高"),
        ("normal", "中"),
        ("low", "低"),
    ];

    private static readonly (string Key, string Label)[] DateOptions =
    [
        ("all", "全部日期"),
        ("today", "今天"),
        ("week", "本周"),
        ("overdue", "已逾期"),
        ("none", "无截止"),
    ];

    private static readonly (string Key, string Label)[] SortOptions =
    [
        ("smart", "智能排序"),
        ("due", "按截止时间"),
        ("priority", "按优先级"),
        ("created", "按创建时间"),
    ];

    private readonly ITodoService _todos;
    private readonly CompletionStyleHub _styles;
    private readonly ThemeManager _themes;

    private string _statusKey = "all";
    private string _priorityKey = "all";
    private string _dateKey = "all";
    private string _sortKey = "smart";
    private bool _suppress;
    private bool _subscribed;
    private bool _styleSubscribed;
    private bool _themeSubscribed;
    private bool _deferRefresh;
    private DispatcherTimer? _toggleRefreshTimer;
    private long? _focusTodoId;

    public TodoView(ITodoService todos, CompletionStyleHub styles, ThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(themes);

        _todos = todos;
        _styles = styles;
        _themes = themes;

        InitializeComponent();
        BuildFilters();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _deferRefresh = false;
        _toggleRefreshTimer?.Stop();

        if (!_subscribed)
        {
            _todos.Changed += OnTodosChanged;
            _subscribed = true;
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

        _ = RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _toggleRefreshTimer?.Stop();

        if (_subscribed)
        {
            _todos.Changed -= OnTodosChanged;
            _subscribed = false;
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

    private void OnCompletionStyleChanged(CompletionStyle style) => _ = RefreshAsync();

    private void OnMotionChanged() => _ = RefreshAsync();

    /// <summary>系统“减少动画”随主题字典重建，需要重新构图标以尊重最新设置。</summary>
    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => _ = RefreshAsync();

    private void OnTodosChanged()
    {
        if (_deferRefresh)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
            return;
        }

        _ = RefreshAsync();
    }

    /// <summary>勾选后等 180ms 动效播完再重建列表，避免重建打断图标动画。</summary>
    private void DeferRefreshForAnimation()
    {
        _deferRefresh = true;

        if (_toggleRefreshTimer is null)
        {
            _toggleRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _toggleRefreshTimer.Tick += OnDeferredRefreshTick;
        }

        _toggleRefreshTimer.Stop();
        _toggleRefreshTimer.Start();
    }

    private void OnDeferredRefreshTick(object? sender, EventArgs e)
    {
        _toggleRefreshTimer?.Stop();
        _deferRefresh = false;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var items = await _todos.ListAsync(BuildFilter(), CancellationToken.None);
            BuildList(items);

            var stats = await _todos.CompletionStatsAsync(
                DateOnly.MinValue,
                DateOnly.MaxValue,
                CancellationToken.None);

            CompletionText.Text =
                $"完成率 {stats.CompletionRate:P0}（{stats.Completed}/{stats.Total}）";
            FooterText.Text = items.Count == 0
                ? HasActiveFilters() ? "没有匹配的待办。" : "还没有待办，点击右上角新建。"
                : $"共 {items.Count} 条待办。";
        }
        catch (Exception ex)
        {
            TodoList.Children.Clear();
            FooterText.Text = $"加载待办失败：{ex.Message}";
        }
    }

    private TodoFilter BuildFilter() => new()
    {
        Status = _statusKey switch
        {
            "active" => TodoStatus.Active,
            "completed" => TodoStatus.Completed,
            "overdue" => TodoStatus.Overdue,
            _ => TodoStatus.All,
        },
        Priority = _priorityKey switch
        {
            "high" => Priority.High,
            "normal" => Priority.Normal,
            "low" => Priority.Low,
            _ => null,
        },
        DateRange = _dateKey switch
        {
            "today" => TodoDateRange.Today,
            "week" => TodoDateRange.ThisWeek,
            "overdue" => TodoDateRange.Overdue,
            "none" => TodoDateRange.NoDueDate,
            _ => TodoDateRange.All,
        },
        Sort = _sortKey switch
        {
            "due" => TodoSort.DueDate,
            "priority" => TodoSort.Priority,
            "created" => TodoSort.CreatedAt,
            _ => TodoSort.Smart,
        },
    };

    private bool HasActiveFilters() =>
        _statusKey != "all"
        || _priorityKey != "all"
        || _dateKey != "all"
        || _sortKey != "smart";

    private void BuildFilters()
    {
        _suppress = true;
        try
        {
            FilterBar.Children.Clear();
            FilterBar.Children.Add(BuildGroup("状态", StatusOptions, _statusKey, OnStatusChecked));
            FilterBar.Children.Add(BuildGroup("优先级", PriorityOptions, _priorityKey, OnPriorityChecked));
            FilterBar.Children.Add(BuildGroup("截止日期", DateOptions, _dateKey, OnDateChecked));
            FilterBar.Children.Add(BuildGroup("排序", SortOptions, _sortKey, OnSortChecked));

            var reset = new Button
            {
                Content = "重置",
                Style = (Style)FindResource("Theme.Button.Ghost"),
                Margin = new Thickness(0, 14, 0, 6),
            };
            reset.Click += OnResetClick;
            FilterBar.Children.Add(reset);
        }
        finally
        {
            _suppress = false;
        }
    }

    private static FrameworkElement BuildGroup(
        string title,
        IReadOnlyList<(string Key, string Label)> options,
        string selected,
        RoutedEventHandler handler)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 14, 6) };
        group.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.FindResource("Theme.Text.Caption"),
        });

        var choices = new WrapPanel();
        foreach (var (key, label) in options)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = key,
                GroupName = $"TodoFilter{title}",
                Style = (Style)Application.Current.FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 4, 6, 0),
                IsChecked = key == selected,
            };
            radio.Checked += handler;
            choices.Children.Add(radio);
        }

        group.Children.Add(choices);
        return group;
    }

    private void OnStatusChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string key })
        {
            return;
        }

        _statusKey = key;
        _ = RefreshAsync();
    }

    private void OnPriorityChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string key })
        {
            return;
        }

        _priorityKey = key;
        _ = RefreshAsync();
    }

    private void OnDateChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string key })
        {
            return;
        }

        _dateKey = key;
        _ = RefreshAsync();
    }

    private void OnSortChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string key })
        {
            return;
        }

        _sortKey = key;
        _ = RefreshAsync();
    }

    private async void OnResetClick(object sender, RoutedEventArgs e)
    {
        _statusKey = "all";
        _priorityKey = "all";
        _dateKey = "all";
        _sortKey = "smart";
        BuildFilters();
        await RefreshAsync();
    }

    private void BuildList(IReadOnlyList<TodoItem> items)
    {
        TodoList.Children.Clear();

        if (items.Count == 0)
        {
            TodoList.Children.Add(new TextBlock
            {
                Text = HasActiveFilters() ? "没有匹配的待办。" : "还没有待办，点击右上角新建。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                Margin = new Thickness(2, 12, 0, 0),
            });
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var item in items)
        {
            TodoList.Children.Add(BuildTodoCard(item, today));
        }
    }

    private Border BuildTodoCard(TodoItem item, DateOnly today)
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
        toggle.Checked += OnTodoToggle;
        toggle.Unchecked += OnTodoToggle;
        grid.Children.Add(toggle);

        if (_focusTodoId == item.Id)
        {
            _focusTodoId = null;
            Dispatcher.BeginInvoke(
                new Action(() => toggle.Focus()),
                DispatcherPriority.Input);
        }

        var main = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(main, 1);

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

        main.Children.Add(title);

        if (item.Note.Length > 0)
        {
            main.Children.Add(new TextBlock
            {
                Text = item.Note,
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        grid.Children.Add(main);

        var due = new TextBlock
        {
            Text = TodoDueFormatter.Format(item, today),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = !item.Done && item.DueDate is { } date && date < today
                ? (Brush)FindResource("Theme.DangerBrush")
                : (Brush)FindResource("Theme.TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(due, 2);
        grid.Children.Add(due);

        var priorityTag = BuildPriorityTag(item.Priority);
        Grid.SetColumn(priorityTag, 3);
        grid.Children.Add(priorityTag);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(actions, 4);

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
        var (text, brush) = priority switch
        {
            Priority.High => ("高", (Brush)FindResource("Theme.DangerBrush")),
            Priority.Low => ("低", (Brush)FindResource("Theme.TextTertiaryBrush")),
            _ => ("中", (Brush)FindResource("Theme.AccentBrush")),
        };

        var tag = new Border
        {
            Style = (Style)FindResource("Theme.Chip"),
            Padding = new Thickness(7, 1, 7, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        tag.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = brush,
        };
        return tag;
    }

    private async void OnTodoToggle(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not CheckBox { Tag: long id } toggle)
        {
            return;
        }

        _focusTodoId = id;
        DeferRefreshForAnimation();

        if (toggle.Content is CompletionGlyph glyph)
        {
            glyph.Done = toggle.IsChecked == true;
        }

        try
        {
            await _todos.ToggleDoneAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"更新待办失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TodoEditorWindow(null, DateOnly.FromDateTime(DateTime.Now))
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
            await RefreshAsync();
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

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TodoItem item })
        {
            return;
        }

        var dialog = new TodoEditorWindow(item, item.DueDate ?? DateOnly.FromDateTime(DateTime.Now))
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } updated)
        {
            return;
        }

        try
        {
            await _todos.UpdateAsync(updated, CancellationToken.None);
            await RefreshAsync();
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

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TodoItem item })
        {
            return;
        }

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"删除待办「{item.Text}」？",
            "拾光",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _todos.DeleteAsync(new[] { item.Id }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"删除待办失败：{ex.Message}",
                "拾光",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
