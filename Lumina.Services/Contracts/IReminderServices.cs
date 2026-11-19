using Lumina.Domain.Enums;

namespace Lumina.Services.Contracts;

/// <summary>一条到点提醒；由日程/倒计时来源按各自的提醒档位生成。</summary>
public sealed record ReminderCandidate(
    string TriggerKey,
    ReminderRoute Route,
    long EntityId,
    DateOnly OccurrenceDate,
    ReminderOffset Offset,
    DateTimeOffset TriggerAt,
    DateTimeOffset TargetAt,
    string Title,
    string Body);

/// <summary>调度器派发结果；<paramref name="AppBarRequested"/> 表示应用内提醒条是否应展示。</summary>
public sealed record ReminderNotification(
    string TriggerKey,
    ReminderRoute Route,
    long EntityId,
    DateOnly OccurrenceDate,
    ReminderOffset Offset,
    DateTimeOffset TargetAt,
    string Title,
    string Body,
    bool SystemDelivered,
    bool AppBarRequested);

/// <summary>提醒来源：在给定时间窗内返回本来源的候选提醒。</summary>
public interface IReminderSource
{
    Task<IReadOnlyList<ReminderCandidate>> CandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
