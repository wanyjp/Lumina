using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Contracts;
using Lumina.Services.Vault;

namespace Lumina.UI.Wpf.Vault;

/// <summary>P3-08 密码生成器：长度、大写/数字/符号开关、重新生成、复制与回填。
/// P3-09：复制走安全通道，不进入剪贴板历史，并按保险库策略定时清除。</summary>
public partial class PasswordGeneratorWindow : Window
{
    private readonly ISecureClipboard _clipboard;
    private bool _loading = true;

    public PasswordGeneratorWindow(ISecureClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);

        _clipboard = clipboard;
        InitializeComponent();
        _loading = false;
        Loaded += OnLoaded;
    }

    public string? ChosenPassword { get; private set; }

    private Border[] StrengthSegments => [Strength1, Strength2, Strength3, Strength4];

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLengthText();
        Generate();
    }

    private void OnLengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        UpdateLengthText();
        Generate();
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Generate();
    }

    private void OnRegenerate(object sender, RoutedEventArgs e) => Generate();

    private void Generate()
    {
        var options = new PasswordGeneratorOptions
        {
            Length = (int)LengthSlider.Value,
            Uppercase = UppercaseBox.IsChecked == true,
            Digits = DigitsBox.IsChecked == true,
            Symbols = SymbolsBox.IsChecked == true,
        };

        var password = PasswordGenerator.Generate(options);
        ResultBox.Text = password;
        PasswordStrengthVisuals.Apply(StrengthSegments, StrengthLabel, password);
        StatusText.Text = string.Empty;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultBox.Text))
        {
            return;
        }

        StatusText.Text = _clipboard.CopySecret(ResultBox.Text) switch
        {
            { Success: true, AutoClear: true } result => $"已复制，{result.ClearAfterSeconds} 秒后自动清除",
            { Success: true } => "已复制到剪贴板",
            _ => "复制失败",
        };
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultBox.Text))
        {
            return;
        }

        ChosenPassword = ResultBox.Text;
        DialogResult = true;
    }

    private void UpdateLengthText() => LengthText.Text = ((int)LengthSlider.Value).ToString();
}
