using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>提醒去重端口：5 分钟窗口内同一触发键只触发一次，超过上限按时间淘汰。</summary>
public interface IReminderDedupStore
{
    bool Contains(string triggerKey);

    void Mark(string triggerKey, DateTimeOffset firedAt);

    int Count();

    /// <summary>只保留最近 <paramref name="target"/> 条，返回删除数量。</summary>
    int TrimTo(int target);
}

/// <summary>
/// 倒计时存储端口：P1-12 读取锚点计算滚动后的下次发生日；P3-03 补齐单条读取与增删改。
/// </summary>
public interface ICountdownStore
{
    IReadOnlyList<CountdownItem> QueryAll();

    CountdownItem? Find(long id);

    long Insert(CountdownItem item, DateTimeOffset now);

    void Update(CountdownItem item, DateTimeOffset now);

    int Delete(IEnumerable<long> ids);
}
