using Lumina.Domain.Enums;
using Lumina.Services.Contracts;

namespace Lumina.Services.Reminders;

/// <summary>
/// 日程提醒来源：取查询窗口附近的展开实例，按每条日程自己的提醒档位生成候选；
/// 已完成日程不提醒，周期实例用 (Id, 实例日期) 生成独立去重键。
/// </summary>
public sealed class ScheduleReminderSource : IReminderSource
{
    private readonly ICalendarService _calendar;

    public ScheduleReminderSource(ICalendarService calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _calendar = calendar;
    }

    public async Task<IReadOnlyList<ReminderCandidate>> CandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var start = DateOnly.FromDateTime(from.Date).AddDays(-1);
        var end = DateOnly.FromDateTime(to.Date).AddDays(1);
        var events = await _calendar.EventsBetweenAsync(start, end, cancellationToken);
        var candidates = new List<ReminderCandidate>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in events)
        {
            if (item.Done || item.Reminder == ReminderOffset.None)
            {
                continue;
            }

            var target = ReminderPlanner.ScheduleTarget(item);
            var trigger = ReminderPlanner.TriggerAt(target, item.Reminder);
            var key = ReminderPlanner.ScheduleKey(item, item.Reminder);

            if (trigger < from || trigger > to || !keys.Add(key))
            {
                continue;
            }

            candidates.Add(new ReminderCandidate(
                key,
                ReminderRoute.Schedule,
                item.Id,
                item.StartDate,
                item.Reminder,
                trigger,
                target,
                $"日程提醒 · {item.Title}",
                ReminderPlanner.ScheduleBody(item)));
        }

        return candidates;
    }
}
