using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Lumina.Services.Vault;

namespace Lumina.UI.Wpf.Vault;

/// <summary>
/// P3-08 保险库三栏界面：左分类、中列表与搜索、右详情；附密码显隐、强度条、
/// 生成器入口、新增/编辑/删除与收藏。锁定态仍复用 P3-07 的解锁面板。
/// </summary>
public partial class VaultView : UserControl
{
    private readonly IVaultService _vault;
    private readonly IClipboardWriter _clipboard;
    private readonly ISecureClipboard _secure;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _statusTimer;

    private IReadOnlyList<VaultEntry> _allEntries = [];
    private IReadOnlyList<VaultEntry> _visibleEntries = [];
    private VaultEntry? _selected;
    private VaultCategory _category = VaultCategory.All;
    private string _keyword = string.Empty;
    private bool _passwordVisible;
    private bool _busy;
    private bool _suppressAutoLock;
    private bool _suppressClearClipboard;
    private bool _suppressCategory;
    private bool _suppressSearch;

    public VaultView(IVaultService vault, IClipboardWriter clipboard, ISecureClipboard secureClipboard)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(secureClipboard);

        _vault = vault;
        _clipboard = clipboard;
        _secure = secureClipboard;
        InitializeComponent();

        _searchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _searchTimer.Tick += OnSearchTick;

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        _statusTimer.Tick += OnStatusTick;

        SetupQuestionBox.ItemsSource = new[]
        {
            "我的第一只宠物叫什么？",
            "我的出生城市是？",
            "我最喜欢的一本书是？",
            "我的小学名称是？",
        };
        SetupQuestionBox.SelectedIndex = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private Border[] StrengthSegments => [Strength1, Strength2, Strength3, Strength4];

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vault.Changed -= OnVaultChanged;
        _vault.Changed += OnVaultChanged;
        _vault.LockStateChanged -= OnVaultChanged;
        _vault.LockStateChanged += OnVaultChanged;
        _secure.Cleared -= OnSecureClipboardCleared;
        _secure.Cleared += OnSecureClipboardCleared;

        await SafeRefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vault.Changed -= OnVaultChanged;
        _vault.LockStateChanged -= OnVaultChanged;
        _secure.Cleared -= OnSecureClipboardCleared;
        _searchTimer.Stop();
        _statusTimer.Stop();
    }

    private void OnVaultChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => _ = SafeRefreshAsync()));
            return;
        }

        _ = SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            // 刷新失败不能静默：保留当前内容，同时把原因告诉用户。
            SetStatus($"刷新失败：{ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        if (!_vault.IsConfigured)
        {
            ShowPanel(SetupScroll);
            HeaderActions.Visibility = Visibility.Collapsed;
            SummaryLine.Text = "首次使用 · 设置主密码";
            SetupError.Visibility = Visibility.Collapsed;
            SetupPasswordBox.Focus();
            return;
        }

        if (_vault.IsLocked)
        {
            _allEntries = [];
            _visibleEntries = [];
            _selected = null;
            EntryList.Children.Clear();
            HidePassword();
            ClearDetail();
            ShowPanel(LockScroll);
            HeaderActions.Visibility = Visibility.Collapsed;
            SummaryLine.Text = "本地加密 · 已锁定";
            UnlockError.Visibility = Visibility.Collapsed;
            UnlockButton.IsEnabled = true;
            UnlockPasswordBox.Focus();
            return;
        }

        ShowPanel(WorkspaceCard);
        HeaderActions.Visibility = Visibility.Visible;
        await LoadEntriesAsync(preserveSelection: _selected is not null);
    }

    private async Task LoadEntriesAsync(bool preserveSelection, long? preferId = null)
    {
        if (_vault.IsLocked)
        {
            await RefreshAsync();
            return;
        }

        var all = await _vault.ListAsync(VaultCategory.All);
        if (_vault.IsLocked)
        {
            await RefreshAsync();
            return;
        }

        _allEntries = all;
        _visibleEntries = FilterEntries(all);
        UpdateCategoryCounts();

        var targetId = preferId ?? (preserveSelection ? _selected?.Id : null);
        _selected = targetId is long id
            ? all.FirstOrDefault(entry => entry.Id == id)
            : null;
        _selected ??= _visibleEntries.FirstOrDefault();

        RenderEntryList();
        await RenderDetailAsync();
        SyncAutoLockBox();
        SyncClearClipboardBox();
        UpdateSummary();
    }

    private IReadOnlyList<VaultEntry> FilterEntries(IReadOnlyList<VaultEntry> all)
    {
        IEnumerable<VaultEntry> query = all;

        query = _category switch
        {
            VaultCategory.Favorite => query.Where(entry => entry.Favorite),
            VaultCategory.All => query,
            _ => query.Where(entry => entry.Category == _category),
        };

        if (!string.IsNullOrWhiteSpace(_keyword))
        {
            query = query.Where(entry =>
                Contains(entry.Title, _keyword)
                || Contains(entry.Username, _keyword)
                || Contains(entry.Url, _keyword));
        }

        return query.ToArray();
    }

    private void UpdateCategoryCounts()
    {
        AllCategory.Content = $"全部  {_allEntries.Count}";
        LoginCategory.Content = $"登录  {Count(VaultCategory.Login)}";
        BankCategory.Content = $"银行卡  {Count(VaultCategory.BankCard)}";
        IdCategory.Content = $"证件  {Count(VaultCategory.IdDocument)}";
        SecureNoteCategory.Content = $"安全笔记  {Count(VaultCategory.SecureNote)}";
        FavoriteCategory.Content = $"收藏  {_allEntries.Count(entry => entry.Favorite)}";
    }

    private int Count(VaultCategory category) =>
        _allEntries.Count(entry => entry.Category == category);

    private void RenderEntryList()
    {
        EntryList.Children.Clear();
        var selectedId = _selected?.Id;

        foreach (var entry in _visibleEntries)
        {
            EntryList.Children.Add(BuildEntryRow(entry, entry.Id == selectedId));
        }
    }

    private Border BuildEntryRow(VaultEntry entry, bool isSelected)
    {
        var row = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
        };
        row.SetResourceReference(
            Border.BackgroundProperty,
            isSelected ? "Theme.AccentSoftBrush" : "Theme.FieldBrush");
        row.SetResourceReference(
            Border.BorderBrushProperty,
            isSelected ? "Theme.AccentBrush" : "Theme.FieldLineBrush");
        AutomationProperties.SetName(row, $"条目 {entry.Title}");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var star = new Button
        {
            Content = entry.Favorite ? "★" : "☆",
            Width = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        star.SetResourceReference(
            Button.ForegroundProperty,
            entry.Favorite ? "Theme.AccentBrush" : "Theme.TextTertiaryBrush");
        AutomationProperties.SetName(
            star,
            entry.Favorite ? $"取消收藏 {entry.Title}" : $"收藏 {entry.Title}");
        star.Click += async (_, args) =>
        {
            args.Handled = true;
            await ToggleFavoriteAsync(entry);
        };
        grid.Children.Add(star);

        var avatar = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        avatar.SetResourceReference(Border.BackgroundProperty, "Theme.AccentSoftBrush");
        var initial = new TextBlock
        {
            Text = Initial(entry.Title),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        initial.SetResourceReference(TextBlock.ForegroundProperty, "Theme.AccentBrush");
        avatar.Child = initial;
        Grid.SetColumn(avatar, 1);
        grid.Children.Add(avatar);

        var textPanel = new StackPanel
        {
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock
        {
            Text = entry.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextBrush");
        AutomationProperties.SetName(title, $"条目 {entry.Title}");
        var subtitle = new TextBlock
        {
            Text = EntrySubtitle(entry),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        subtitle.SetResourceReference(TextBlock.FontSizeProperty, "Theme.FontSize.Caption");
        textPanel.Children.Add(title);
        textPanel.Children.Add(subtitle);
        Grid.SetColumn(textPanel, 2);
        grid.Children.Add(textPanel);

        var category = new TextBlock
        {
            Text = CategoryLabel(entry.Category),
            VerticalAlignment = VerticalAlignment.Center,
        };
        category.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");
        category.SetResourceReference(TextBlock.FontSizeProperty, "Theme.FontSize.Caption");
        Grid.SetColumn(category, 3);
        grid.Children.Add(category);

        row.Child = grid;
        row.MouseLeftButtonUp += async (_, args) =>
        {
            args.Handled = true;
            await SelectEntryAsync(entry);
        };
        return row;
    }

    private async Task SelectEntryAsync(VaultEntry entry)
    {
        if (_selected?.Id == entry.Id)
        {
            return;
        }

        HidePassword();
        _selected = entry;
        RenderEntryList();
        await RenderDetailAsync();
    }

    private async Task RenderDetailAsync()
    {
        if (_selected is not { } entry)
        {
            ClearDetail();
            return;
        }

        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;

        DetailTitle.Text = entry.Title;
        DetailCategory.Text = CategoryLabel(entry.Category)
            + (entry.Favorite ? " · 已收藏" : string.Empty);
        DetailInitial.Text = Initial(entry.Title);
        UsernameText.Text = string.IsNullOrWhiteSpace(entry.Username) ? "—" : entry.Username;
        CopyUsernameButton.IsEnabled = !string.IsNullOrWhiteSpace(entry.Username);

        UrlBlock.Visibility = string.IsNullOrWhiteSpace(entry.Url)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UrlText.Text = entry.Url ?? string.Empty;

        CreatedText.Text = $"创建：{FormatTime(entry.CreatedAt)}";
        UpdatedText.Text = $"更新：{FormatTime(entry.UpdatedAt)}";
        UpdateUsedText(entry);

        FavoriteButton.Content = entry.Favorite ? "★" : "☆";
        AutomationProperties.SetName(
            FavoriteButton,
            entry.Favorite ? "取消收藏" : "收藏条目");

        NoteBlock.Visibility = Visibility.Collapsed;
        NoteText.Text = string.Empty;
        HidePassword();

        if (entry.EncryptedNote is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var note = await _vault.RevealNoteAsync(entry.Id);
            if (_selected?.Id == entry.Id && !string.IsNullOrWhiteSpace(note))
            {
                NoteText.Text = note;
                NoteBlock.Visibility = Visibility.Visible;
            }
        }
        catch (VaultLockedException)
        {
            // 自动锁定与详情加载竞争：下一次刷新会切回锁屏。
        }
    }

    /// <summary>锁定或清空选择时移除详情中的明文，避免残留在可视化树与内存引用里。</summary>
    private void ClearDetail()
    {
        DetailTitle.Text = string.Empty;
        DetailCategory.Text = string.Empty;
        DetailInitial.Text = string.Empty;
        UsernameText.Text = "—";
        UrlText.Text = string.Empty;
        NoteText.Text = string.Empty;
        CreatedText.Text = string.Empty;
        UpdatedText.Text = string.Empty;
        UsedText.Text = string.Empty;
        PasswordText.Text = string.Empty;
        UrlBlock.Visibility = Visibility.Collapsed;
        NoteBlock.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Collapsed;
        DetailEmpty.Visibility = Visibility.Visible;
    }

    private void UpdateUsedText(VaultEntry entry) =>
        UsedText.Text = $"最近使用：{FormatTime(entry.UsedAt)}";

    private void HidePassword()
    {
        _passwordVisible = false;
        PasswordEyeButton.Content = "显示";
        AutomationProperties.SetName(PasswordEyeButton, "显示密码");

        var hasPassword = _selected?.EncryptedPassword is { Length: > 0 };
        PasswordText.Text = hasPassword ? "••••••••" : "（未设置）";
        PasswordStrengthVisuals.Apply(
            StrengthSegments,
            StrengthLabel,
            password: null,
            hidden: hasPassword);
    }

    private async void OnTogglePasswordClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        if (_passwordVisible)
        {
            HidePassword();
            return;
        }

        try
        {
            var password = await _vault.RevealPasswordAsync(entry.Id);
            if (_selected?.Id != entry.Id)
            {
                return;
            }

            PasswordText.Text = string.IsNullOrEmpty(password) ? "（未设置）" : password;
            _passwordVisible = true;
            PasswordEyeButton.Content = "隐藏";
            AutomationProperties.SetName(PasswordEyeButton, "隐藏密码");
            PasswordStrengthVisuals.Apply(StrengthSegments, StrengthLabel, password);

            _selected = entry with { UsedAt = DateTimeOffset.Now };
            UpdateUsedText(_selected);
        }
        catch (Exception exception)
        {
            SetStatus($"无法显示密码：{exception.Message}");
        }
    }

    private async void OnCopyPasswordClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        try
        {
            var password = await _vault.RevealPasswordAsync(entry.Id);
            if (string.IsNullOrEmpty(password))
            {
                SetStatus("该条目没有设置密码");
                return;
            }

            var result = _secure.CopySecret(password);
            if (!result.Success)
            {
                SetStatus(result.Failure == SecureCopyFailure.ClipboardUnavailable
                    ? "复制失败，请显示密码后手动复制"
                    : "该条目没有设置密码");
                return;
            }

            SetStatus(result.AutoClear
                ? $"密码已复制，{result.ClearAfterSeconds} 秒后自动清除"
                : "密码已复制到剪贴板");
            if (_selected?.Id == entry.Id)
            {
                _selected = entry with { UsedAt = DateTimeOffset.Now };
                UpdateUsedText(_selected);
            }
        }
        catch (Exception exception)
        {
            SetStatus($"复制失败：{exception.Message}");
        }
    }

    private void OnCopyUsernameClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry || string.IsNullOrWhiteSpace(entry.Username))
        {
            return;
        }

        SetStatus(
            _clipboard.TryWritePlainText(entry.Username)
                ? "用户名已复制到剪贴板"
                : "复制失败");
    }

    private async void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        try
        {
            await _vault.SetFavoriteAsync(entry.Id, !entry.Favorite);
            SetStatus(entry.Favorite ? "已取消收藏" : "已加入收藏");
        }
        catch (Exception exception)
        {
            SetStatus($"操作失败：{exception.Message}");
        }
    }

    private async Task ToggleFavoriteAsync(VaultEntry entry)
    {
        try
        {
            await _vault.SetFavoriteAsync(entry.Id, !entry.Favorite);
        }
        catch (Exception exception)
        {
            SetStatus($"操作失败：{exception.Message}");
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VaultEditorWindow(_vault, _secure)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true)
        {
            await ShowSavedEntryAsync(dialog.SavedEntryId);
            SetStatus("条目已创建");
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        var dialog = new VaultEditorWindow(_vault, _secure, entry.Id)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true)
        {
            await ShowSavedEntryAsync(dialog.SavedEntryId);
            SetStatus("条目已更新");
        }
    }

    private async Task ShowSavedEntryAsync(long id)
    {
        _category = VaultCategory.All;
        _keyword = string.Empty;

        _suppressCategory = true;
        AllCategory.IsChecked = true;
        _suppressCategory = false;

        _suppressSearch = true;
        SearchBox.Text = string.Empty;
        _suppressSearch = false;

        await LoadEntriesAsync(preserveSelection: false, preferId: id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry)
        {
            return;
        }

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"删除「{entry.Title}」后无法恢复。",
            "删除条目",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _vault.DeleteEntryAsync(entry.Id);
            _selected = null;
            await LoadEntriesAsync(preserveSelection: false);
            SetStatus("条目已删除");
        }
        catch (Exception exception)
        {
            SetStatus($"删除失败：{exception.Message}");
        }
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordGeneratorWindow(_secure)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true && dialog.ChosenPassword is { Length: > 0 } password)
        {
            var result = _secure.CopySecret(password);
            SetStatus(result.Success
                ? (result.AutoClear
                    ? $"生成的密码已复制，{result.ClearAfterSeconds} 秒后自动清除"
                    : "生成的密码已复制到剪贴板")
                : "复制失败，请手动选择密码");
        }
    }

    private async void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressCategory || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var category = tag switch
        {
            "login" => VaultCategory.Login,
            "bank" => VaultCategory.BankCard,
            "id" => VaultCategory.IdDocument,
            "note" => VaultCategory.SecureNote,
            "favorite" => VaultCategory.Favorite,
            _ => VaultCategory.All,
        };

        if (_category == category)
        {
            return;
        }

        _category = category;
        HidePassword();
        await LoadEntriesAsync(preserveSelection: false);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_suppressSearch)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void OnSearchTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        _keyword = SearchBox.Text.Trim();
        await LoadEntriesAsync(preserveSelection: false);
    }

    private async void OnAutoLockChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded
            || _suppressAutoLock
            || AutoLockBox.SelectedItem is not ComboBoxItem { Tag: string tag }
            || !int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return;
        }

        try
        {
            await _vault.SetAutoLockMinutesAsync(minutes);
            UpdateSummary();
            SetStatus(minutes <= 0 ? "已设为仅手动锁定" : $"闲置 {minutes} 分钟自动锁定");
        }
        catch (Exception exception)
        {
            SetStatus($"设置失败：{exception.Message}");
        }
    }

    private async void OnClearClipboardChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded
            || _suppressClearClipboard
            || ClearClipboardBox.SelectedItem is not ComboBoxItem { Tag: string tag }
            || !int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return;
        }

        try
        {
            await _vault.SetClearClipboardSecondsAsync(seconds);
            SetStatus(seconds <= 0
                ? "复制密码后不再自动清除剪贴板"
                : $"复制密码后 {seconds} 秒自动清除剪贴板");
        }
        catch (Exception exception)
        {
            SetStatus($"设置失败：{exception.Message}");
        }
    }

    private void SyncClearClipboardBox()
    {
        var seconds = _vault.ClearClipboardSeconds;
        _suppressClearClipboard = true;
        try
        {
            foreach (ComboBoxItem item in ClearClipboardBox.Items)
            {
                if (item.Tag is string tag
                    && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    && value == seconds)
                {
                    ClearClipboardBox.SelectedItem = item;
                    return;
                }
            }

            ClearClipboardBox.SelectedIndex = 1;
        }
        finally
        {
            _suppressClearClipboard = false;
        }
    }

    private void OnSecureClipboardCleared(SecureClipboardClearReason reason)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSecureClipboardCleared(reason)));
            return;
        }

        switch (reason)
        {
            case SecureClipboardClearReason.Expired:
                SetStatus("剪贴板已自动清除");
                break;
            case SecureClipboardClearReason.Locked:
                SetStatus("保险库已锁定，剪贴板已清除");
                break;
            case SecureClipboardClearReason.Unavailable:
                SetStatus("剪贴板被占用，未能自动清除，请手动覆盖");
                break;
        }
    }

    private void SyncAutoLockBox()
    {
        var minutes = _vault.AutoLockMinutes;
        _suppressAutoLock = true;
        try
        {
            foreach (ComboBoxItem item in AutoLockBox.Items)
            {
                if (item.Tag is string tag
                    && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    && value == minutes)
                {
                    AutoLockBox.SelectedItem = item;
                    return;
                }
            }

            AutoLockBox.SelectedIndex = 1;
        }
        finally
        {
            _suppressAutoLock = false;
        }
    }

    private void UpdateSummary()
    {
        var autoLock = _vault.AutoLockMinutes;
        var policy = autoLock <= 0 ? "仅手动锁定" : $"闲置 {autoLock} 分钟锁定";
        SummaryLine.Text = $"本地加密 · 已解锁 · {_allEntries.Count} 个条目 · {policy}";
        ListCountText.Text = string.IsNullOrWhiteSpace(_keyword)
            ? $"共 {_visibleEntries.Count} 条"
            : $"找到 {_visibleEntries.Count} 条";
    }

    private async void OnSetupClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetupError.Visibility = Visibility.Collapsed;
        var password = SetupPasswordBox.Password;
        if (password.Length < VaultService.MinimumPasswordLength)
        {
            ShowSetupError($"主密码至少需要 {VaultService.MinimumPasswordLength} 位。");
            return;
        }

        if (!string.Equals(password, SetupConfirmBox.Password, StringComparison.Ordinal))
        {
            ShowSetupError("两次输入的主密码不一致。");
            return;
        }

        var question = SetupQuestionBox.Text.Trim();
        if (question.Length == 0)
        {
            ShowSetupError("请选择或填写一个安全问题。");
            return;
        }

        var answer = SetupAnswerBox.Text.Trim();
        if (answer.Length == 0)
        {
            ShowSetupError("请填写安全答案，用于忘记主密码时找回。");
            return;
        }

        try
        {
            SetBusy(true);
            await _vault.SetupAsync(password, question, answer);
            SetupPasswordBox.Clear();
            SetupConfirmBox.Clear();
            SetupAnswerBox.Clear();
            await SafeRefreshAsync();
        }
        catch (Exception exception)
        {
            ShowSetupError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnForgotPasswordClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VaultRecoveryWindow(_vault)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true)
        {
            await SafeRefreshAsync();
            SetStatus("主密码已重置");
        }
    }

    private void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VaultChangePasswordWindow(_vault)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true)
        {
            SetStatus("主密码已修改");
        }
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        UnlockError.Visibility = Visibility.Collapsed;
        try
        {
            SetBusy(true);
            var unlocked = await _vault.UnlockAsync(UnlockPasswordBox.Password);
            if (!unlocked)
            {
                ShowUnlockError("主密码不正确，请重试。");
                UnlockPasswordBox.SelectAll();
                UnlockPasswordBox.Focus();
                return;
            }

            UnlockPasswordBox.Clear();
            await SafeRefreshAsync();
        }
        catch (VaultException exception)
        {
            ShowUnlockError(exception.Message);
        }
        catch (Exception exception)
        {
            ShowUnlockError($"解锁失败：{exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnUnlockKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        OnUnlockClick(this, new RoutedEventArgs());
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        HidePassword();
        _vault.Lock();
    }

    private void ShowPanel(UIElement active)
    {
        SetupScroll.Visibility = ReferenceEquals(active, SetupScroll) ? Visibility.Visible : Visibility.Collapsed;
        LockScroll.Visibility = ReferenceEquals(active, LockScroll) ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceCard.Visibility = ReferenceEquals(active, WorkspaceCard) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowSetupError(string message)
    {
        SetupError.Text = message;
        SetupError.Visibility = Visibility.Visible;
    }

    private void ShowUnlockError(string message)
    {
        UnlockError.Text = message;
        UnlockError.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SetupButton.IsEnabled = !busy;
        UnlockButton.IsEnabled = !busy;
        SetupPasswordBox.IsEnabled = !busy;
        SetupConfirmBox.IsEnabled = !busy;
        UnlockPasswordBox.IsEnabled = !busy;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnStatusTick(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        StatusText.Text = string.Empty;
    }

    private static string EntrySubtitle(VaultEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Username)
            ? entry.Username
            : !string.IsNullOrWhiteSpace(entry.Url)
                ? entry.Url
                : "未填写用户名";

    private static string CategoryLabel(VaultCategory category) => category switch
    {
        VaultCategory.BankCard => "银行卡",
        VaultCategory.IdDocument => "证件",
        VaultCategory.SecureNote => "安全笔记",
        _ => "登录",
    };

    private static string Initial(string title) =>
        string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant();

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "—";

    private static bool Contains(string? value, string needle) =>
        value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
