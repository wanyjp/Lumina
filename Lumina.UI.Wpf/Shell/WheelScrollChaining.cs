using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lumina.UI.Wpf.Shell;

/// <summary>
/// 滚轮滚动接力：光标停在内层 <see cref="ScrollViewer"/>（白噪音音源列表、截图历史三行视口等）上时，
/// 先滚内层；内层已达边界或无法滚动时，把滚轮交给外层页面。
/// <para>
/// WPF 的内层 ScrollViewer 会消费滚轮且不向上传递，导致“鼠标停在卡片上就滚不动整页”。
/// 这里在主壳统一接管：找到还能在该方向滚动的最近一层 ScrollViewer，把事件重定向给它，
/// 既保留系统的滚动步长，又实现浏览器那样的接力。
/// </para>
/// </summary>
internal static class WheelScrollChaining
{
    /// <summary>在主壳根节点上挂一次即可覆盖所有模块内容。</summary>
    public static void Attach(UIElement root) => root.PreviewMouseWheel += OnPreviewMouseWheel;

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not UIElement root)
        {
            return;
        }

        var viewers = CollectViewers(e.OriginalSource as DependencyObject, root);
        if (viewers.Count == 0)
        {
            return;
        }

        // 内层到边界后由外层接管；这里直接滚动，避免再走一遍 ScrollContentPresenter 的事件链。
        e.Handled = true;
        ScrollChain(viewers, e.Delta > 0, Math.Max(1, Math.Abs(e.Delta) / 120));
    }

    /// <summary>
    /// 逐行接力：每一行都先从内向外找「该方向还能滚」的 ScrollViewer 再滚，
    /// 因此内层滚到边界的那一瞬，同一手势剩余的滚动量会自然落到外层页面。
    /// </summary>
    private static void ScrollChain(List<ScrollViewer> viewers, bool up, int notches)
    {
        // 与系统一致：滚轮行数由「设置 → 鼠标」决定，-1 表示整页滚动。
        var lines = SystemParameters.WheelScrollLines;

        if (lines < 0)
        {
            for (var i = 0; i < notches; i++)
            {
                var target = FirstScrollable(viewers, up);
                if (target is null)
                {
                    return;
                }

                if (up)
                {
                    target.PageUp();
                }
                else
                {
                    target.PageDown();
                }
            }

            return;
        }

        var steps = Math.Max(1, lines) * notches;
        for (var i = 0; i < steps; i++)
        {
            var target = FirstScrollable(viewers, up);
            if (target is null)
            {
                return;
            }

            if (up)
            {
                target.LineUp();
            }
            else
            {
                target.LineDown();
            }
        }
    }

    private static ScrollViewer? FirstScrollable(List<ScrollViewer> viewers, bool up) =>
        viewers.FirstOrDefault(viewer => CanScroll(viewer, up));

    /// <summary>滚轮向上（delta &gt; 0）需要还有上方内容；向下需要还有下方内容。</summary>
    private static bool CanScroll(ScrollViewer viewer, bool up) =>
        viewer.IsVisible
        && viewer.ScrollableHeight > 0
        && (up ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight);

    /// <summary>由内向外收集滚动容器；到挂载根（主壳）为止。</summary>
    private static List<ScrollViewer> CollectViewers(DependencyObject? source, UIElement root)
    {
        var viewers = new List<ScrollViewer>();
        var node = source;

        while (node is not null)
        {
            if (node is ScrollViewer viewer)
            {
                viewers.Add(viewer);
            }

            if (ReferenceEquals(node, root))
            {
                break;
            }

            node = ParentOf(node);
        }

        return viewers;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(node);
        }

        if (node is ContentElement content)
        {
            return ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent;
        }

        return LogicalTreeHelper.GetParent(node);
    }
}
