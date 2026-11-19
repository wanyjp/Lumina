using Lumina.Domain.Enums;

namespace Lumina.Services.Focus;

/// <summary>
/// 一次专注会话的结束事件。`SessionId` 为幂等键，同一会话只允许发布一次；
/// P3-01 专注统计订阅此事件写入 `focus_session`。
/// </summary>
public sealed record FocusSessionEvent(
    Guid SessionId,
    FocusSource Source,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    bool Completed);

/// <summary>会话结束事件的发布中心；按 `SessionId` 去重，避免计时器与番茄重复计数。</summary>
public sealed class FocusSessionHub
{
    private readonly HashSet<Guid> _published = [];

    public event Action<FocusSessionEvent>? Published;

    public void Publish(FocusSessionEvent session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_published)
        {
            if (!_published.Add(session.SessionId))
            {
                return;
            }
        }

        Published?.Invoke(session);
    }
}
