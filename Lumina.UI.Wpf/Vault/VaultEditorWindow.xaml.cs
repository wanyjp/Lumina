using System.Windows;
using System.Windows.Controls;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Vault;

/// <summary>P3-08 条目编辑窗：名称、分类、用户名、密码（显隐 + 强度 + 生成）、网址、备注、收藏。
/// P3-09：生成器内复制走安全通道。</summary>
public partial class VaultEditorWindow : Window
{
    private readonly IVaultService _vault;
    private readonly ISecureClipboard _clipboard;
    private readonly long _entryId;

    private string _password = string.Empty;
    private bool _syncing;

    public VaultEditorWindow(IVaultService vault, ISecureClipboard clipboard, long entryId = 0)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentOutOfRangeException.ThrowIfNegative(entryId);

        _vault = vault;
        _clipboard = clipboard;
        _entryId = entryId;

        InitializeComponent();

        TitleText.Text = entryId == 0 ? "新增条目" : "编辑条目";
        Title = TitleText.Text;
        Loaded += OnLoaded;
    }

    public long SavedEntryId { get; private set; }

    private Border[] StrengthSegments => [Strength1, Strength2, Strength3, Strength4];

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_entryId == 0)
        {
            CategoryBox.SelectedIndex = 0;
            UpdateStrength();
            TitleBox.Focus();
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var draft = await _vault.ReadDraftAsync(_entryId);
            if (draft is null)
            {
                ShowError("条目不存在或已被删除。");
                return;
            }

            TitleBox.Text = draft.Title;
            UsernameBox.Text = draft.Username ?? string.Empty;
            UrlBox.Text = draft.Url ?? string.Empty;
            NoteBox.Text = draft.Note ?? string.Empty;
            FavoriteBox.IsChecked = draft.Favorite;
            SelectCategory(draft.Category);

            SetPassword(draft.Password ?? string.Empty);
            TitleBox.Focus();
        }
        catch (Exception exception)
        {
            ShowError($"读取条目失败：{exception.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnMaskedPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _password = PasswordMasked.Password;
        UpdateStrength();
    }

    private void OnPlainPasswordChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || PasswordPlain.Visibility != Visibility.Visible)
        {
            return;
        }

        _password = PasswordPlain.Text;
        UpdateStrength();
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            if (PasswordPlain.Visibility != Visibility.Visible)
            {
                PasswordPlain.Text = _password;
                PasswordPlain.Visibility = Visibility.Visible;
                PasswordMasked.Visibility = Visibility.Collapsed;
                PasswordEyeButton.Content = "隐藏";
                PasswordPlain.Focus();
                PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
            }
            else
            {
                PasswordMasked.Password = _password;
                PasswordMasked.Visibility = Visibility.Visible;
                PasswordPlain.Visibility = Visibility.Collapsed;
                PasswordEyeButton.Content = "显示";
                PasswordMasked.Focus();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnGeneratePassword(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordGeneratorWindow(_clipboard)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true && dialog.ChosenPassword is { Length: > 0 } password)
        {
            SetPassword(password);
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowError("请填写条目名称。");
            TitleBox.Focus();
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var draft = new VaultEntryDraft
            {
                Category = SelectedCategory(),
                Title = title,
                Username = UsernameBox.Text,
                Password = string.IsNullOrEmpty(_password) ? null : _password,
                Url = UrlBox.Text,
                Note = NoteBox.Text,
                Favorite = FavoriteBox.IsChecked == true,
            };

            var saved = await _vault.SaveDraftAsync(draft, _entryId);
            SavedEntryId = saved.Id;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ShowError($"保存失败：{exception.Message}");
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetPassword(string password)
    {
        _password = password;
        _syncing = true;
        try
        {
            PasswordMasked.Password = password;
            PasswordPlain.Text = password;
        }
        finally
        {
            _syncing = false;
        }

        UpdateStrength();
    }

    private void UpdateStrength() =>
        PasswordStrengthVisuals.Apply(StrengthSegments, StrengthLabel, _password);

    private void SelectCategory(VaultCategory category)
    {
        var tag = category.ToString();
        foreach (ComboBoxItem item in CategoryBox.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                CategoryBox.SelectedItem = item;
                return;
            }
        }

        CategoryBox.SelectedIndex = 0;
    }

    private VaultCategory SelectedCategory() =>
        CategoryBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<VaultCategory>(tag, out var category)
            ? category
            : VaultCategory.Login;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
