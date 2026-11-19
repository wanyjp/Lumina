using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Focus;

/// <summary>
/// 专注统计采集与聚合实现（P3-01）。
/// 写入按 <see cref="FocusSession.SessionKey"/> 幂等；聚合按本地自然日切分，
/// 会话归属结束日，跨天不拆分。
/// </summary>
public sealed class FocusTrackerService : IFocusTracker
{
    private readonly IFocusSessionStore _store;
    private readonly ITodoService? _todos;
    private readonly Func<DateTimeOffset> _now;

    public FocusTrackerService(
        IFocusSessionStore store,
        ITodoService? todos = null,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _todos = todos;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public Task RecordSessionAsync(FocusSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(session.SessionKey))
        {
            throw new ArgumentException("会话缺少幂等键。", nameof(session));
        }

        if (session.EndedAt < session.StartedAt)
        {
            throw new ArgumentException("会话结束时间早于开始时间。", nameof(session));
        }

        if (session.DurationSeconds < 0)
        {
            throw new ArgumentException("会话时长不能为负。", nameof(session));
        }

        _store.Add(session);
        return Task.CompletedTask;
    }

    public async Task<PeriodAggregate> AggregateAsync(
        PeriodKind kind,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var period = FocusAggregator.Resolve(kind, offset, _now());
        var sessions = _store.EndedBetween(period.StartInclusive, period.EndExclusive);

        TodoCompletionStats? todos = null;
        if (_todos is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            todos = await _todos
                .CompletionStatsAsync(period.Start, period.End, cancellationToken)
                .ConfigureAwait(false);
        }

        return FocusAggregator.Compute(period, sessions, todos);
    }
}
