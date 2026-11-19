using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Lumina.UI.Wpf.Controls;

/// <summary>
/// 滚轮路由：Slider、ComboBox 等控件会吞掉 MouseWheel（用于改值或收起下拉），
/// 导致鼠标停在音量滑杆上时整页无法滚动。这里在隧道阶段拦截，
/// 把滚轮交给最近一个可滚动的 ScrollViewer。
/// </summary>
public static class WheelRouter
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer || !IsValueControl(e.OriginalSource))
        {
            return;
        }

        var target = FindScrollable(sender as DependencyObject);
        if (target is null)
        {
            return;
        }

        target.ScrollToVerticalOffset(target.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>事件源是否落在会自行消费滚轮的控件内。</summary>
    private static bool IsValueControl(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Slider or ComboBox or ScrollBar)
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static ScrollViewer? FindScrollable(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is ScrollViewer viewer && viewer.ScrollableHeight > 0)
            {
                return viewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
