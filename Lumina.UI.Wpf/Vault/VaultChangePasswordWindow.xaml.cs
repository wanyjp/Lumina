using System.Windows;
using Lumina.Services.Contracts;
using Lumina.Services.Vault;

namespace Lumina.UI.Wpf.Vault;

/// <summary>已解锁状态下的修改主密码：验证当前密码并重新加密全部条目。</summary>
public partial class VaultChangePasswordWindow : Window
{
    private readonly IVaultService _vault;
    private bool _busy;

    public VaultChangePasswordWindow(IVaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        _vault = vault;
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        var current = CurrentPasswordBox.Password;
        var password = NewPasswordBox.Password;

        if (current.Length == 0)
        {
            ShowError("请输入当前主密码。");
            return;
        }

        if (password.Length < VaultService.MinimumPasswordLength)
        {
            ShowError($"新主密码至少需要 {VaultService.MinimumPasswordLength} 位。");
            return;
        }

        if (!string.Equals(password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowError("两次输入的新主密码不一致。");
            return;
        }

        try
        {
            _busy = true;
            SaveButton.IsEnabled = false;
            await _vault.ChangePasswordAsync(current, password);
            DialogResult = true;
            Close();
        }
        catch (VaultException exception)
        {
            ShowError(exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ShowError(exception.Message);
        }
        catch (Exception exception)
        {
            ShowError($"修改失败：{exception.Message}");
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
