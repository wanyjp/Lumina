using Lumina.Domain.Enums;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Reminders;

/// <summary>
/// 倒计时提醒来源：按锚点滚动计算下次发生日，目标日期锚定本地 09:00；
/// 重复实例使用发生日期生成独立去重键。
/// </summary>
public sealed class CountdownReminderSource : IReminderSource
{
    private readonly ICountdownStore _store;

    public CountdownReminderSource(ICountdownStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<ReminderCandidate>> CandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromDate = DateOnly.FromDateTime(from.Date);
        var candidates = new List<ReminderCandidate>();

        foreach (var item in _store.QueryAll())
        {
            if (item.Reminder == ReminderOffset.None)
            {
                continue;
            }

            var occurrence = CountdownOccurrenceCalculator.Next(item, fromDate);
            if (occurrence is null)
            {
                continue;
            }

            var target = ReminderPlanner.CountdownTarget(occurrence.Value);
            var trigger = ReminderPlanner.TriggerAt(target, item.Reminder);
            if (trigger < from || trigger > to)
            {
                continue;
            }

            var key = ReminderPlanner.CountdownKey(item, occurrence.Value, item.Reminder);
            candidates.Add(new ReminderCandidate(
                key,
                ReminderRoute.Countdown,
                item.Id,
                occurrence.Value,
                item.Reminder,
                trigger,
                target,
                $"倒计时 · {item.Name}",
                $"{occurrence.Value.Month} 月 {occurrence.Value.Day} 日（{ReminderPlanner.OffsetLabel(item.Reminder)}）"));
        }

        return Task.FromResult<IReadOnlyList<ReminderCandidate>>(candidates);
    }
}
