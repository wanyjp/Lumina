using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>便签存储端口：P3-05 新增。</summary>
public interface INoteStore
{
    /// <summary>按「置顶优先、更新时间倒序」返回全部便签。</summary>
    IReadOnlyList<NoteItem> QueryAll();

    NoteItem? Find(long id);

    long Insert(NoteItem item, DateTimeOffset now);

    void Update(NoteItem item, DateTimeOffset now);

    int Delete(IEnumerable<long> ids);
}
