namespace Lumina.Services.Clipboard;

/// <summary>剪贴板列表关心的按键子集；与 WPF <c>Key</c> 解耦以便单元测试。</summary>
public enum ClipboardKey
{
    None = 0,
    Enter = 1,
    Delete = 2,
    Up = 3,
    Down = 4,
    Escape = 5,
}

/// <summary>按键解析后的列表动作。</summary>
public enum ClipboardCommand
{
    None = 0,
    ToggleDetail = 1,
    DeleteCurrent = 2,
    MoveUp = 3,
    MoveDown = 4,
    CancelSelection = 5,
    ClearSearch = 6,
    CollapseDetail = 7,
}

/// <summary>
/// 键盘路由：搜索框获得焦点时屏蔽 Enter/Delete/↑↓，避免与文本编辑冲突；
/// Esc 始终可用，按「退出多选 → 清空搜索 → 收起详情」的顺序逐层后退。
/// </summary>
public static class ClipboardKeyboardRouter
{
    public static ClipboardCommand Route(
        ClipboardKey key,
        bool textInputFocused,
        bool selectionMode,
        bool hasSearch,
        bool hasExpanded)
    {
        if (key == ClipboardKey.Escape)
        {
            if (selectionMode)
            {
                return ClipboardCommand.CancelSelection;
            }

            if (hasSearch)
            {
                return ClipboardCommand.ClearSearch;
            }

            return hasExpanded ? ClipboardCommand.CollapseDetail : ClipboardCommand.None;
        }

        // 输入框内不拦截其余按键，保持正常编辑行为。
        if (textInputFocused)
        {
            return ClipboardCommand.None;
        }

        return key switch
        {
            ClipboardKey.Enter => ClipboardCommand.ToggleDetail,
            ClipboardKey.Delete => ClipboardCommand.DeleteCurrent,
            ClipboardKey.Up => ClipboardCommand.MoveUp,
            ClipboardKey.Down => ClipboardCommand.MoveDown,
            _ => ClipboardCommand.None,
        };
    }

    /// <summary>按偏移移动焦点索引，越界钳制在有效范围内；空列表返回 -1。</summary>
    public static int MoveIndex(int current, int delta, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return delta >= 0 ? 0 : count - 1;
        }

        return Math.Clamp(current + delta, 0, count - 1);
    }
}
