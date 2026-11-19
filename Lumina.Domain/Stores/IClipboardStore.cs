using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>
/// 剪贴板历史仓储。P2-01 负责写入与时间窗去重；P2-02 追加保留上限淘汰、
/// 固定/取消固定、软删/硬删与过期软删清理；P2-03 追加回贴前移与备注标签持久化。
/// 列表读取只返回未删除记录。
/// </summary>
public interface IClipboardStore
{
    /// <summary>
    /// 保存一条剪贴板记录；时间窗内同哈希记录合并并前移，返回最终记录。
    /// <paramref name="mergeDifferentSources"/> 为 true 时忽略来源合并，否则只与同来源记录合并。
    /// 写入后按 <paramref name="retentionLimit"/> 淘汰最旧的非固定记录（0 = 不限制）。
    /// </summary>
    ClipboardSaveResult Save(
        ClipboardItem item,
        DateTimeOffset now,
        TimeSpan duplicateWindow,
        int retentionLimit,
        bool mergeDifferentSources = true);

    /// <summary>按固定优先、固定时间倒序，其余按更新时间倒序读取未删除记录。</summary>
    IReadOnlyList<ClipboardItem> QueryRecent(int limit);

    /// <summary>按上限淘汰最旧的非固定记录并返回被删除条目；固定条目不参与计数。</summary>
    IReadOnlyList<ClipboardItem> EvictOldestNonPinned(int retentionLimit);

    /// <summary>固定或取消固定；固定时写入 <paramref name="now"/> 作为稳定排序时间。</summary>
    bool SetPinned(long id, bool pinned, DateTimeOffset now);

    /// <summary>软删：只标记删除时间，保留内容与附件以便恢复。</summary>
    bool SoftDelete(long id, DateTimeOffset now);

    /// <summary>硬删：直接移除记录并返回原条目，调用方负责清理附件文件。</summary>
    ClipboardItem? HardDelete(long id);

    /// <summary>清理早于 <paramref name="cutoff"/> 的软删记录并返回条目，调用方负责清理附件文件。</summary>
    IReadOnlyList<ClipboardItem> PurgeSoftDeletedBefore(DateTimeOffset cutoff);

    /// <summary>
    /// 刷新更新时间以把条目前移置顶（回贴成功后调用）；返回更新后的条目，
    /// 记录不存在或已软删时返回 null。
    /// </summary>
    ClipboardItem? Touch(long id, DateTimeOffset now);

    /// <summary>
    /// 写入备注与标签（空集合表示清空）；不改变更新时间，因此不会改变列表顺序。
    /// 返回更新后的条目，记录不存在或已软删时返回 null。
    /// </summary>
    ClipboardItem? UpdateAnnotation(long id, string? note, IReadOnlyList<string> tags);

    /// <summary>
    /// 编辑文本类条目的内容并刷新更新时间；类型、URL、MIME、归一化文本与哈希由调用方重算。
    /// 图片/文件条目不调用本方法。返回更新后的条目，不存在或已软删时返回 null。
    /// </summary>
    ClipboardItem? UpdateContent(long id, ClipboardItem content, DateTimeOffset now);
}
