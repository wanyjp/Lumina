namespace Lumina.Domain.Models;

/// <summary>
/// 剪贴板保存结果：<paramref name="Merged"/> 表示命中了时间窗去重并合并到已有记录；
/// <paramref name="Evicted"/> 为本次写入后因超出保留上限被淘汰的非固定条目。
/// </summary>
public sealed record ClipboardSaveResult(
    ClipboardItem Item,
    bool Merged,
    IReadOnlyList<ClipboardItem> Evicted);
