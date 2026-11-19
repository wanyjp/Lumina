using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>
/// 日程存储端口。查询按“开始日期 ≤ 区间结束 且 结束日期（空则同开始）≥ 区间开始”取交集，
/// 保证跨天日程在中间日期也能被命中；周期展开在 P1-07 由规则引擎完成。
/// </summary>
public interface IScheduleStore
{
    IReadOnlyList<ScheduleEvent> QueryRange(DateOnly start, DateOnly end);

    /// <summary>取出开始日期不晚于 <paramref name="end"/> 的全部周期母记录，供动态展开。</summary>
    IReadOnlyList<ScheduleEvent> QueryRecurring(DateOnly end);

    ScheduleEvent? Find(long id);

    long Insert(ScheduleEvent item, DateTimeOffset now);

    void Update(ScheduleEvent item, DateTimeOffset now);

    int Delete(IEnumerable<long> ids);
}
