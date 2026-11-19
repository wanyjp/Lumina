using Lumina.Domain.Models;

namespace Lumina.Services.Clipboard;

/// <summary>
/// 多选状态：固定条目不可勾选（必须先取消固定才允许删除），因此
/// <see cref="Toggle"/> 与 <see cref="SelectAll"/> 都会跳过固定项。
/// </summary>
public sealed class ClipboardSelection
{
    private readonly HashSet<long> _ids = [];

    /// <summary>是否处于多选模式；进入后列表显示勾选框。</summary>
    public bool IsActive { get; private set; }

    public int Count => _ids.Count;

    public bool IsSelected(long id) => _ids.Contains(id);

    /// <summary>进入多选模式并清空既选集合。</summary>
    public void Begin()
    {
        _ids.Clear();
        IsActive = true;
    }

    /// <summary>退出多选模式并清空既选集合。</summary>
    public void Cancel()
    {
        _ids.Clear();
        IsActive = false;
    }

    public void Clear() => _ids.Clear();

    /// <summary>切换单条选中态；固定项返回 false 且不改变状态。</summary>
    public bool Toggle(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!IsActive || item.Pinned)
        {
            return false;
        }

        if (_ids.Remove(item.Id))
        {
            return false;
        }

        _ids.Add(item.Id);
        return true;
    }

    /// <summary>全选当前可见条目中的非固定项，返回新增的数量。</summary>
    public int SelectAll(IEnumerable<ClipboardItem> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);

        if (!IsActive)
        {
            return 0;
        }

        var added = 0;
        foreach (var item in visible)
        {
            if (item.Pinned)
            {
                continue;
            }

            if (_ids.Add(item.Id))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>当前选中 id 快照，按升序返回便于稳定断言。</summary>
    public IReadOnlyList<long> Snapshot() => _ids.OrderBy(id => id).ToArray();

    /// <summary>可见集合变化后剔除已不可选的 id（固定或已删除）。</summary>
    public void Prune(IEnumerable<ClipboardItem> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);

        var allowed = new HashSet<long>();
        foreach (var item in visible)
        {
            if (!item.Pinned)
            {
                allowed.Add(item.Id);
            }
        }

        _ids.RemoveWhere(id => !allowed.Contains(id));
    }
}
