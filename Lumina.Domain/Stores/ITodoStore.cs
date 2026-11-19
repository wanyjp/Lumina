using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>待办存储端口：全量列表、按截止日期区间、单条读取与增删改。</summary>
public interface ITodoStore
{
    IReadOnlyList<TodoItem> QueryAll();

    IReadOnlyList<TodoItem> QueryRange(DateOnly start, DateOnly end);

    TodoItem? Find(long id);

    long Insert(TodoItem item, DateTimeOffset now);

    void Update(TodoItem item, DateTimeOffset now);

    int Delete(IEnumerable<long> ids);
}
