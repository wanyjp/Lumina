namespace Lumina.Domain.Capture;

/// <summary>
/// 泛型快照撤销/重做栈：编辑器用它同时记录"当前底图 + 标注元素"，
/// 使裁剪/旋转这类会改变底图的操作也能与标注编辑共用同一条撤销链。
/// 与 <see cref="AnnotationHistory"/> 的语义一致（提交清空重做栈、超容量淘汰最旧快照）。
/// </summary>
public sealed class StateHistory<T>
    where T : class
{
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    private T _current;

    public StateHistory(T initial, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "撤销容量至少为 2。");
        }

        Capacity = capacity;
        _current = initial;
    }

    public int Capacity { get; }

    public T Current => _current;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>提交一次新状态；重做栈清空。</summary>
    public void Commit(T next)
    {
        ArgumentNullException.ThrowIfNull(next);

        _undo.Add(_current);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _current = next;
    }

    /// <summary>撤销；无可撤销时返回 null 且状态不变。</summary>
    public T? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_current);
        _current = previous;
        return _current;
    }

    /// <summary>重做；无可重做时返回 null 且状态不变。</summary>
    public T? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_current);
        _current = next;
        return _current;
    }
}
