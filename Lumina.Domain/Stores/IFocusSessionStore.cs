using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>
/// 专注会话存储（P3-01）。`SessionKey` 唯一，重复写入按幂等处理。
/// 查询以 UTC 区间比较，避免用户切换时区后区间错位。
/// </summary>
public interface IFocusSessionStore
{
    /// <summary>写入一条会话；`SessionKey` 已存在时返回 0，不覆盖历史记录。</summary>
    int Add(FocusSession session);

    /// <summary>按结束时间（UTC 左闭右开）查询会话，按结束时间升序。</summary>
    IReadOnlyList<FocusSession> EndedBetween(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive);

    /// <summary>最近 N 条会话，按结束时间倒序；供统计页与排查使用。</summary>
    IReadOnlyList<FocusSession> Recent(int limit);

    /// <summary>会话总数。</summary>
    int Count();
}
