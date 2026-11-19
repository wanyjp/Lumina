using System.Windows;
using Lumina.Services.Contracts;
using Lumina.Services.Vault;

namespace Lumina.UI.Wpf.Vault;

/// <summary>忘记主密码：用安全答案验证后设置新主密码，条目会重新加密。</summary>
public partial class VaultRecoveryWindow : Window
{
    private readonly IVaultService _vault;
    private bool _busy;

    public VaultRecoveryWindow(IVaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        _vault = vault;
        InitializeComponent();

        QuestionText.Text = vault.RecoveryQuestion ?? "（该保险库未设置安全问题）";
        ResetButton.IsEnabled = vault.HasRecovery;
    }

    private async void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !_vault.HasRecovery)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        var answer = AnswerBox.Text.Trim();
        if (answer.Length == 0)
        {
            ShowError("请输入安全答案。");
            return;
        }

        var password = NewPasswordBox.Password;
        if (password.Length < VaultService.MinimumPasswordLength)
        {
            ShowError($"主密码至少需要 {VaultService.MinimumPasswordLength} 位。");
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
            ResetButton.IsEnabled = false;
            var reset = await _vault.ResetPasswordAsync(answer, password);
            if (!reset)
            {
                ShowError("安全答案不正确，请重试。");
                AnswerBox.SelectAll();
                AnswerBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception exception) when (exception is ArgumentException
                                               or InvalidOperationException
                                               or VaultException)
        {
            ShowError(exception.Message);
        }
        catch (Exception exception)
        {
            ShowError($"重置失败：{exception.Message}");
        }
        finally
        {
            _busy = false;
            ResetButton.IsEnabled = _vault.HasRecovery;
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
