using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Lumina.Services.Contracts;
using Lumina.SystemAdapters.ScreenCapture;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lumina.App.Capture;

/// <summary>选窗器条目展示模型。</summary>
public sealed class WindowPickerItem
{
    public nint Handle { get; init; }

    public string Title { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public BitmapImage? Icon { get; init; }

    /// <summary>
    /// 列表项的默认字符串表示。WPF 会用它作为 UIA 名称，未重写时读屏与自动化只能读到类型名。
    /// </summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(ProcessName) ? Title : $"{Title}（{ProcessName}）";
}

/// <summary>
/// 窗口截图的选窗对话框：枚举当前会话可见窗口（排除本进程），
/// 双击或确认返回所选句柄。放在应用层以引用 SystemAdapters 的枚举实现。
/// </summary>
public partial class WindowPickerWindow : Window
{
    private WindowPickerItem? _selected;

    private WindowPickerWindow()
    {
        InitializeComponent();
        LoadWindows();
    }

    /// <summary>弹出选窗器；返回所选窗口句柄，取消返回 null。</summary>
    public static nint? Pick(Window? owner)
    {
        var dialog = new WindowPickerWindow
        {
            Owner = owner,
        };
        dialog.ShowDialog();
        return dialog._selected is null ? null : dialog._selected.Handle;
    }

    private void LoadWindows()
    {
        var items = new List<WindowPickerItem>();

        try
        {
            foreach (var window in WindowEnumeration.Enumerate())
            {
                items.Add(new WindowPickerItem
                {
                    Handle = window.Handle,
                    Title = window.Title,
                    ProcessName = string.IsNullOrEmpty(window.ProcessName) ? "未知进程" : window.ProcessName,
                    Icon = ToImage(window.IconPng),
                });
            }
        }
        catch (Exception ex)
        {
            AppLogCaptureEnumerationFailed(ex);
        }

        WindowList.ItemsSource = items;

        if (items.Count > 0)
        {
            WindowList.SelectedIndex = 0;
        }
        else
        {
            ConfirmButton.IsEnabled = false;
        }
    }

    private static BitmapImage? ToImage(byte[]? iconPng)
    {
        if (iconPng is null || iconPng.Length == 0)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(iconPng);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void AppLogCaptureEnumerationFailed(Exception exception)
    {
        // 枚举失败不阻断对话框：展示空列表即可。
        System.Diagnostics.Debug.WriteLine($"窗口枚举失败：{exception.Message}");
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = WindowList.SelectedItem as WindowPickerItem;
        ConfirmButton.IsEnabled = _selected is not null;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selected is not null)
        {
            Close();
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null)
        {
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _selected = null;
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _selected = null;
            Close();
        }
        else if (e.Key == Key.Return && _selected is not null)
        {
            e.Handled = true;
            Close();
        }
    }
}
