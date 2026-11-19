using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lumina.Widgets;

/// <summary>
/// 小组件拖动命中的统一判断：只有落在按钮、输入框、下拉、滚动条等可交互控件上时，
/// 才把点击交给控件处理；其余区域（文字、图标、空白）都用于整卡拖动。
/// 注意不能把 <see cref="UserControl"/> 当作交互控件，否则整卡永远拖不动。
/// </summary>
internal static class WidgetDragHelper
{
    public static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is UserControl)
            {
                return false;
            }

            if (current is ButtonBase
                or TextBoxBase
                or ComboBox
                or ScrollBar
                or Slider
                or Thumb)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
