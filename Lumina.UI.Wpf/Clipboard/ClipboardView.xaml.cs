using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clipboard;

/// <summary>
/// P2-03 剪贴板页：分类页签（带数量）、实时搜索、标签筛选、列表内展开详情、
/// 回贴、多选清理、滑动编辑/删除与键盘操作，并提供四种区分场景的空态。
/// </summary>
public partial class ClipboardView : UserControl
{
    private readonly ClipboardBoardService _board;
    private readonly IClipboardMonitor _monitor;
    private readonly ClipboardFloatController? _float;
    private readonly ISettingsStore _settings;
    private readonly ClipboardSelection _selection = new();
    private readonly DispatcherTimer _searchTimer;
    private readonly List<ClipboardCard> _cards = [];

    private ClipboardListView _view = ClipboardListView.Empty;
    private ClipboardFilter _filter = ClipboardFilter.All;
    private string _search = string.Empty;
    private string? _tag;
    private long? _expandedId;
    private int _focusIndex = -1;
    private long? _focusId;
    private bool _suppress;
    private bool _subscribed;
    private IDisposable? _settingsSubscription;
    private bool _monitorSubscribed;
    private EmptyStateAction _emptyAction = EmptyStateAction.None;

    /// <summary>双击回填后短暂抑制展开：列表重建后鼠标抬起可能落在新卡片上，避免误展开详情。</summary>
    private DateTime _suppressExpandUntil = DateTime.MinValue;

    public ClipboardView(
        ClipboardBoardService board,
        IClipboardMonitor monitor,
        ClipboardFloatController? floatController,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);

        _board = board;
        _monitor = monitor;
        _float = floatController;
        _settings = settings;

        InitializeComponent();
        FloatButton.Visibility = floatController is null ? Visibility.Collapsed : Visibility.Visible;

        _searchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _searchTimer.Tick += OnSearchTimerTick;

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>当前页面可见条目的 id，供测试与走查断言。</summary>
    public IReadOnlyList<long> VisibleIds => _view.Items.Select(item => item.Id).ToArray();

    /// <summary>双击回填成功后触发；浮窗据此自动隐藏。</summary>
    public event EventHandler? PasteBackCompleted;

    /// <summary>双击回填后是否按设置最小化宿主窗口；浮窗内关闭此行为。</summary>
    public bool MinimizeHostOnPaste { get; set; } = true;

    /// <summary>浮窗模式：去掉页面外框与重复标题，保留页签/搜索/详情/滑动编辑等完整能力。</summary>
    public void ApplyFloatMode()
    {
        HeaderTitleText.Visibility = Visibility.Collapsed;
        FloatButton.Visibility = Visibility.Collapsed;
        PageCard.Background = Brushes.Transparent;
        PageCard.BorderBrush = Brushes.Transparent;
        PageCard.BorderThickness = new Thickness(0);
        PageCard.Effect = null;
        PageCard.Padding = new Thickness(0);
        MinimizeHostOnPaste = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _settingsSubscription = _settings.Changed.Subscribe(new SettingsObserver(OnSettingChanged));
            _subscribed = true;
        }

        if (!_monitorSubscribed)
        {
            _monitor.Captured += OnCaptured;
            _monitorSubscribed = true;
        }

        Refresh();
        Dispatcher.BeginInvoke(new Action(() => ListScroll.Focus()), DispatcherPriority.Input);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _searchTimer.Stop();

        if (_monitorSubscribed)
        {
            _monitor.Captured -= OnCaptured;
            _monitorSubscribed = false;
        }

        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
        _subscribed = false;
    }

    private void OnCaptured(ClipboardItem item)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Refresh));
            return;
        }

        Refresh();
    }

    private void OnSettingChanged(SettingChanged change)
    {
        if (!string.Equals(change.Key, PreferenceKeys.ClipboardShowTags, StringComparison.Ordinal))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Refresh));
            return;
        }

        Refresh();
    }

    /// <summary>按当前筛选重新读取并重建列表；同步执行，单次查询上限为 9999 条。</summary>
    public void Refresh()
    {
        if (_suppress)
        {
            return;
        }

        try
        {
            _view = _board.Load(_filter, _search, _tag);
            _selection.Prune(_view.Items);

            // 按 id 记忆焦点：新记录插入头部后，索引会指向错误的条目。
            var focusIndex = _focusId is { } focusId ? IndexOfItem(focusId) : _focusIndex;
            _focusIndex = ClipboardKeyboardRouter.MoveIndex(focusIndex, 0, _view.Items.Count);
            if (_focusIndex < 0)
            {
                _focusId = null;
            }

            var offset = ListScroll.VerticalOffset;

            BuildTabs();
            BuildTagFilter();
            BuildList();
            UpdateSummary();
            UpdateSelectionBar();
            UpdateEmptyState();

            if (offset > 0)
            {
                Dispatcher.BeginInvoke(
                    new Action(() => ListScroll.ScrollToVerticalOffset(offset)),
                    DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            CardList.Children.Clear();
            StatusText.Text = "加载剪贴板失败：" + ex.Message;
            StatusText.Foreground = (Brush)FindResource("Theme.DangerBrush");
        }
    }

    // ---- 页签 ----

    private void BuildTabs()
    {
        _suppress = true;
        try
        {
            TabBar.Children.Clear();

            foreach (var filter in ClipboardFilters.Tabs)
            {
                var count = _view.Counts.For(filter);
                var radio = new RadioButton
                {
                    Content = $"{ClipboardFilters.Label(filter)} {count}",
                    Tag = filter,
                    GroupName = "ClipboardTabs",
                    Style = (Style)FindResource("Theme.ThemeChoice"),
                    Margin = new Thickness(0, 0, 6, 6),
                    IsChecked = filter == _filter,
                };

                radio.Checked += OnTabChecked;
                TabBar.Children.Add(radio);
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: ClipboardFilter filter })
        {
            return;
        }

        _filter = filter;
        _focusIndex = -1;
        _focusId = null;
        Refresh();
    }

    // ---- 标签筛选 ----

    private void BuildTagFilter()
    {
        TagBar.Children.Clear();

        var visible = _board.TagsVisible && _view.Tags.Count > 0;
        TagFilterPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            if (!_board.TagsVisible)
            {
                _tag = null;
            }

            return;
        }

        TagBar.Children.Add(new TextBlock
        {
            Text = "标签",
            Style = (Style)FindResource("Theme.Text.Caption"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
        });

        foreach (var tag in _view.Tags)
        {
            var active = string.Equals(tag, _tag, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = tag,
                Tag = tag,
                Style = (Style)FindResource("Theme.Button.Secondary"),
                Padding = new Thickness(9, 2, 9, 2),
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 6),
                Background = active
                    ? (Brush)FindResource("Theme.AccentSoftBrush")
                    : (Brush)FindResource("Theme.CardSolidBrush"),
                Foreground = active
                    ? (Brush)FindResource("Theme.AccentBrush")
                    : (Brush)FindResource("Theme.TextSecondaryBrush"),
                ToolTip = active ? "再次点击取消标签筛选" : $"只看标签「{tag}」",
            };

            button.Click += OnTagClick;
            TagBar.Children.Add(button);
        }
    }

    private void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        _tag = string.Equals(tag, _tag, StringComparison.OrdinalIgnoreCase) ? null : tag;
        _focusIndex = -1;
        _focusId = null;
        Refresh();
    }

    // ---- 列表 ----

    private void BuildList()
    {
        CardList.Children.Clear();
        _cards.Clear();

        var tagsVisible = _board.TagsVisible;

        foreach (var item in _view.Items)
        {
            var card = new ClipboardCard(
                item,
                expanded: _expandedId == item.Id,
                selectionMode: _selection.IsActive,
                selected: _selection.IsSelected(item.Id),
                tagsVisible: tagsVisible);

            card.CopyRequested += OnCardCopyRequested;
            card.CopyPlainRequested += OnCardCopyPlainRequested;
            card.PasteBackRequested += OnCardPasteBackRequested;
            card.PinRequested += OnCardPinRequested;
            card.EditRequested += OnCardEditRequested;
            card.DeleteRequested += OnCardDeleteRequested;
            card.SelectionToggled += OnCardSelectionToggled;
            card.ExpandToggled += OnCardExpandToggled;

            _cards.Add(card);
            CardList.Children.Add(card);
        }

        ApplyFocus();
    }

    private void ApplyFocus()
    {
        for (var index = 0; index < _cards.Count; index++)
        {
            _cards[index].SetFocused(index == _focusIndex);
        }
    }

    private void FocusCard(int index)
    {
        _focusIndex = ClipboardKeyboardRouter.MoveIndex(index, 0, _cards.Count);
        _focusId = _focusIndex >= 0 && _focusIndex < _cards.Count
            ? _cards[_focusIndex].Item.Id
            : null;
        ApplyFocus();

        if (_focusIndex >= 0 && _focusIndex < _cards.Count)
        {
            _cards[_focusIndex].BringIntoViewSafely();
        }
    }

    private int IndexOfItem(long id)
    {
        for (var index = 0; index < _view.Items.Count; index++)
        {
            if (_view.Items[index].Id == id)
            {
                return index;
            }
        }

        return _focusIndex;
    }

    private void OnCardExpandToggled(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressExpandUntil)
        {
            return;
        }

        if (sender is not ClipboardCard card)
        {
            return;
        }

        FocusCard(_cards.IndexOf(card));
        _expandedId = _expandedId == card.Item.Id ? null : card.Item.Id;
        Refresh();
    }

    private void OnCardSelectionToggled(object? sender, EventArgs e)
    {
        if (sender is not ClipboardCard card)
        {
            return;
        }

        FocusCard(_cards.IndexOf(card));

        if (card.Item.Pinned)
        {
            SetStatus("固定条目需先取消固定才能删除。", isError: true);
            return;
        }

        _selection.Toggle(card.Item);
        Refresh();
    }

    private void OnCardCopyRequested(object? sender, EventArgs e) =>
        CopyCard(sender as ClipboardCard, plainText: false);

    private void OnCardCopyPlainRequested(object? sender, EventArgs e) =>
        CopyCard(sender as ClipboardCard, plainText: true);

    /// <summary>双击回填：复制后抑制误展开，并按设置最小化主窗口返回原应用。</summary>
    private void OnCardPasteBackRequested(object? sender, EventArgs e)
    {
        _suppressExpandUntil = DateTime.UtcNow.AddMilliseconds(800);
        var success = CopyCard(sender as ClipboardCard, plainText: false);

        if (!success)
        {
            return;
        }

        PasteBackCompleted?.Invoke(this, EventArgs.Empty);

        if (!MinimizeHostOnPaste)
        {
            return;
        }

        var minimize = _settings.Get(
            PreferenceKeys.ClipboardMinimizeAfterPaste,
            PreferenceDefaults.ClipboardMinimizeAfterPaste);
        if (minimize && Window.GetWindow(this) is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private bool CopyCard(ClipboardCard? card, bool plainText)
    {
        if (card is null)
        {
            return false;
        }

        var outcome = _board.Copy(card.Item, plainText);
        SetStatus(outcome.Message, isError: !outcome.Success);
        Refresh();
        return outcome.Success;
    }

    private void OnCardPinRequested(object? sender, EventArgs e)
    {
        if (sender is not ClipboardCard card)
        {
            return;
        }

        var target = !card.Item.Pinned;
        if (_board.SetPinned(card.Item.Id, target))
        {
            SetStatus(target ? "已固定，固定条目不参与保留上限计数。" : "已取消固定。", isError: false);
        }
        else
        {
            SetStatus("固定状态更新失败，条目可能已被清理。", isError: true);
        }

        Refresh();
    }

    private void OnCardEditRequested(object? sender, EventArgs e)
    {
        if (sender is not ClipboardCard card)
        {
            return;
        }

        if (card.Item.Kind is ClipboardKind.Text or ClipboardKind.Link or ClipboardKind.Html)
        {
            EditContent(card.Item);
        }
        else
        {
            SetStatus("图片与文件条目不能编辑正文，已打开备注与标签。", isError: false);
            EditAnnotation(card.Item);
        }
    }

    private void OnCardDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is ClipboardCard card)
        {
            DeleteItems([card.Item], "删除这条记录？", single: true);
        }
    }

    private void EditContent(ClipboardItem item)
    {
        var dialog = new ClipboardContentEditorWindow(item)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var outcome = _board.SaveContent(item, dialog.ContentText);
        if (!outcome.Success)
        {
            SetStatus(outcome.Message ?? "保存失败。", isError: true);
            return;
        }

        var kind = outcome.Kind switch
        {
            ClipboardKind.Link => "链接",
            ClipboardKind.Html => "HTML",
            _ => "文本",
        };
        SetStatus($"内容已保存，当前归类为「{kind}」。", isError: false);
        Refresh();
    }

    private void EditAnnotation(ClipboardItem item)
    {
        var dialog = new ClipboardAnnotationWindow(item)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var outcome = _board.SaveAnnotation(item, dialog.NoteText, dialog.TagsText);
        if (!outcome.Success)
        {
            SetStatus(outcome.Message ?? "保存失败。", isError: true);
            Refresh();
            return;
        }

        var message = outcome.Dropped > 0
            ? $"已保存备注与 {outcome.Tags.Count} 个标签；忽略 {outcome.Dropped} 个重复、空白或超出上限的标签。"
            : $"已保存备注与 {outcome.Tags.Count} 个标签。";

        SetStatus(message, isError: false);
        Refresh();
    }

    // ---- 多选 ----

    private void OnFloatClick(object sender, RoutedEventArgs e) => _float?.Show();

    private void OnSelectModeClick(object sender, RoutedEventArgs e)
    {
        if (_selection.IsActive)
        {
            _selection.Cancel();
        }
        else
        {
            if (_view.Items.Count == 0)
            {
                SetStatus("当前没有可选择的条目。", isError: true);
                return;
            }

            _selection.Begin();
            _expandedId = null;
        }

        Refresh();
        UpdateSelectionBar();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        var added = _selection.SelectAll(_view.Items);
        if (added == 0)
        {
            SetStatus("没有可选的未固定条目。", isError: true);
        }

        Refresh();
    }

    private void OnCancelSelectionClick(object sender, RoutedEventArgs e)
    {
        _selection.Cancel();
        Refresh();
    }

    private void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _view.Items.Where(item => _selection.IsSelected(item.Id)).ToArray();
        if (selected.Length == 0)
        {
            SetStatus("请先勾选要删除的条目。", isError: true);
            return;
        }

        DeleteItems(selected, $"删除所选 {selected.Length} 条记录？固定条目不会被删除。", single: false);
    }

    private void DeleteItems(IReadOnlyCollection<ClipboardItem> items, string prompt, bool single)
    {
        var pinnedOnly = items.All(item => item.Pinned);
        if (pinnedOnly)
        {
            SetStatus("固定条目需先取消固定才能删除。", isError: true);
            return;
        }

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            prompt,
            "拾光",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        var outcome = _board.Delete(items);

        if (!single)
        {
            _selection.Cancel();
        }
        else if (_expandedId is { } id && items.Any(item => item.Id == id))
        {
            _expandedId = null;
        }

        if (outcome.SkippedPinned > 0 && outcome.Deleted > 0)
        {
            SetStatus(
                $"已删除 {outcome.Deleted} 条；{outcome.SkippedPinned} 条固定记录已跳过，需先取消固定。",
                isError: false);
        }
        else if (outcome.Deleted > 0)
        {
            SetStatus($"已删除 {outcome.Deleted} 条记录（30 天内可由保留策略清理）。", isError: false);
        }
        else
        {
            SetStatus("没有删除任何记录。", isError: true);
        }

        Refresh();
    }

    private void UpdateSelectionBar()
    {
        SelectionBar.Visibility = _selection.IsActive ? Visibility.Visible : Visibility.Collapsed;
        SelectModeButton.Content = _selection.IsActive ? "退出多选" : "多选";
        SelectionText.Text = $"已选 {_selection.Count} 项";
        DeleteSelectedButton.IsEnabled = _selection.Count > 0;
    }

    // ---- 搜索 ----

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? string.Empty;
        ClearSearchButton.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateSearchPlaceholder();

        // 输入防抖：停止输入 250ms 后才查询，避免每次按键全表扫描。
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchFocusChanged(object sender, RoutedEventArgs e) => UpdateSearchPlaceholder();

    /// <summary>占位文字仅在空且未聚焦时显示，避免插入符落在提示文字笔画上。</summary>
    private void UpdateSearchPlaceholder()
    {
        var show = (SearchBox.Text ?? string.Empty).Length == 0 && !SearchBox.IsKeyboardFocusWithin;
        SearchPlaceholder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplySearch(SearchBox.Text);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Down)
        {
            _searchTimer.Stop();
            ApplySearch(SearchBox.Text);
            ListScroll.Focus();
            e.Handled = true;
        }
    }

    private void ApplySearch(string? text)
    {
        var normalized = ClipboardSearch.Normalize(text);
        if (string.Equals(normalized, _search, StringComparison.Ordinal))
        {
            return;
        }

        _search = normalized;
        _focusIndex = -1;
        _focusId = null;
        Refresh();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        ApplySearch(string.Empty);
        SearchBox.Focus();
    }

    // ---- 空态 ----

    private void UpdateEmptyState()
    {
        var empty = _view.Items.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        CardList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (!empty)
        {
            return;
        }

        _emptyAction = EmptyStateAction.None;
        EmptyAction.Visibility = Visibility.Collapsed;

        if (_view.TotalCount == 0)
        {
            ShowEmpty(
                "\uE8C8",
                "还没有剪贴板记录",
                "复制任意文本、链接、图片或文件，内容会自动出现在这里；密码类内容默认不采集。");
            return;
        }

        if (ClipboardSearch.IsActive(_search))
        {
            ShowEmpty(
                "\uE721",
                $"没有匹配「{_search}」的记录",
                "搜索覆盖正文、备注、标签与来源。可以换个关键词，或清除搜索查看全部。");
            _emptyAction = EmptyStateAction.ClearSearch;
            EmptyAction.Content = "清除搜索";
            EmptyAction.Visibility = Visibility.Visible;
            return;
        }

        if (!string.IsNullOrEmpty(_tag))
        {
            ShowEmpty(
                "\uE8EC",
                $"标签「{_tag}」下没有记录",
                "该标签可能只存在于已被清理的条目上；清除标签筛选可查看全部记录。");
            _emptyAction = EmptyStateAction.ClearTag;
            EmptyAction.Content = "清除标签筛选";
            EmptyAction.Visibility = Visibility.Visible;
            return;
        }

        if (_filter == ClipboardFilter.Pinned)
        {
            ShowEmpty(
                "\uE840",
                "还没有固定条目",
                "固定常用内容后会一直保留，不受保留上限影响，也不会参与自动淘汰。");
            return;
        }

        ShowEmpty(
            "\uE71D",
            $"「{ClipboardFilters.Label(_filter)}」分类暂无记录",
            "切换到「全部」查看其他内容，或复制新的内容后自动归类。");
    }

    private void ShowEmpty(string glyph, string title, string body)
    {
        EmptyGlyph.Text = glyph;
        EmptyTitle.Text = title;
        EmptyBody.Text = body;
    }

    private void OnEmptyActionClick(object sender, RoutedEventArgs e)
    {
        switch (_emptyAction)
        {
            case EmptyStateAction.ClearSearch:
                SearchBox.Text = string.Empty;
                ApplySearch(string.Empty);
                break;
            case EmptyStateAction.ClearTag:
                _tag = null;
                Refresh();
                break;
            default:
                break;
        }
    }

    // ---- 键盘 ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = MapKey(e.Key);
        if (key == ClipboardKey.None)
        {
            return;
        }

        var textInputFocused = Keyboard.FocusedElement is TextBox;
        var command = ClipboardKeyboardRouter.Route(
            key,
            textInputFocused,
            _selection.IsActive,
            ClipboardSearch.IsActive(_search),
            _expandedId is not null);

        if (command == ClipboardCommand.None)
        {
            return;
        }

        e.Handled = true;

        switch (command)
        {
            case ClipboardCommand.ToggleDetail:
                if (CurrentItem() is { } toggle)
                {
                    _expandedId = _expandedId == toggle.Id ? null : toggle.Id;
                    Refresh();
                }

                break;
            case ClipboardCommand.DeleteCurrent:
                if (CurrentItem() is { } target)
                {
                    DeleteItems([target], "删除这条记录？", single: true);
                }

                break;
            case ClipboardCommand.MoveUp:
                FocusCard(_focusIndex - 1);
                break;
            case ClipboardCommand.MoveDown:
                FocusCard(_focusIndex + 1);
                break;
            case ClipboardCommand.CancelSelection:
                _selection.Cancel();
                Refresh();
                break;
            case ClipboardCommand.ClearSearch:
                SearchBox.Text = string.Empty;
                ApplySearch(string.Empty);
                break;
            case ClipboardCommand.CollapseDetail:
                _expandedId = null;
                Refresh();
                break;
        }
    }

    private ClipboardItem? CurrentItem() =>
        _focusIndex >= 0 && _focusIndex < _view.Items.Count ? _view.Items[_focusIndex] : null;

    private static ClipboardKey MapKey(Key key) => key switch
    {
        Key.Enter => ClipboardKey.Enter,
        Key.Delete => ClipboardKey.Delete,
        Key.Up => ClipboardKey.Up,
        Key.Down => ClipboardKey.Down,
        Key.Escape => ClipboardKey.Escape,
        _ => ClipboardKey.None,
    };

    // ---- 状态与摘要 ----

    private void UpdateSummary()
    {
        var parts = new List<string>
        {
            $"共 {_view.TotalCount} 条",
            $"固定 {_view.PinnedCount} 条",
        };

        if (ClipboardSearch.IsActive(_search))
        {
            parts.Add($"命中 {_view.Items.Count} 条");
        }

        if (!_board.TagsVisible)
        {
            parts.Add("标签栏已在设置中隐藏");
        }

        SummaryText.Text = string.Join(" · ", parts);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("Theme.DangerBrush")
            : (Brush)FindResource("Theme.TextTertiaryBrush");
    }

    private enum EmptyStateAction
    {
        None = 0,
        ClearSearch = 1,
        ClearTag = 2,
    }

    private sealed class SettingsObserver(Action<SettingChanged> handler) : IObserver<SettingChanged>
    {
        public void OnNext(SettingChanged value) => handler(value);

        public void OnError(Exception error)
        {
            // 设置流异常不影响页面。
        }

        public void OnCompleted()
        {
            // 无生命周期结束语义。
        }
    }
}
