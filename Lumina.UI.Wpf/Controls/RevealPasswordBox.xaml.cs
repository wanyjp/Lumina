using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Lumina.UI.Wpf.Controls;

/// <summary>
/// 带“眼睛”按钮的密码输入：隐藏态用 PasswordBox，显示态用 TextBox，
/// 两者内容双向同步，供创建/解锁/修改/找回主密码复用。
/// </summary>
public partial class RevealPasswordBox : UserControl
{
    private bool _syncing;

    public RevealPasswordBox()
    {
        InitializeComponent();
    }

    public string Password => HiddenBox.Password;

    /// <summary>设置输入框的无障碍名称（同时作用于隐藏与显示态）。</summary>
    public string InputName
    {
        set
        {
            AutomationProperties.SetName(HiddenBox, value);
            AutomationProperties.SetName(VisibleBox, value);
        }
    }

    public void Clear()
    {
        HiddenBox.Clear();
        VisibleBox.Clear();
    }

    public void SelectAll()
    {
        if (VisibleBox.Visibility == Visibility.Visible)
        {
            VisibleBox.SelectAll();
            VisibleBox.Focus();
            return;
        }

        HiddenBox.Focus();
        HiddenBox.SelectAll();
    }

    public new bool Focus() =>
        VisibleBox.Visibility == Visibility.Visible ? VisibleBox.Focus() : HiddenBox.Focus();

    private void OnHiddenPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            VisibleBox.Text = HiddenBox.Password;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnVisibleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            HiddenBox.Password = VisibleBox.Text;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        var reveal = VisibleBox.Visibility != Visibility.Visible;

        if (reveal)
        {
            VisibleBox.Text = HiddenBox.Password;
            HiddenBox.Visibility = Visibility.Collapsed;
            VisibleBox.Visibility = Visibility.Visible;
            VisibleBox.Focus();
            VisibleBox.CaretIndex = VisibleBox.Text.Length;
        }
        else
        {
            HiddenBox.Password = VisibleBox.Text;
            VisibleBox.Visibility = Visibility.Collapsed;
            HiddenBox.Visibility = Visibility.Visible;
            HiddenBox.Focus();
        }

        RevealGlyph.Text = reveal ? "\uED1A" : "\uE7B3";
        RevealButton.ToolTip = reveal ? "隐藏密码" : "显示密码";
        AutomationProperties.SetName(RevealButton, reveal ? "隐藏密码" : "显示密码");
    }
}
