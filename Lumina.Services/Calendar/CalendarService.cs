using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Calendar;

/// <summary>
/// P1-05 / P1-06 / P1-07 日历服务：按设置中的周起始构建月历模型；
/// 日程查询合并单次记录与周期母记录的动态展开实例，返回与日期区间有交集的事件。
/// </summary>
public sealed class CalendarService : ICalendarService
{
    private const int RecurringCoverageDays = 366;

    private readonly ISettingsStore _settings;
    private readonly IScheduleStore _schedules;

    public CalendarService(ISettingsStore settings, IScheduleStore schedules)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(schedules);

        _settings = settings;
        _schedules = schedules;
    }

    public CalendarMonthModel BuildMonth(int year, int month)
    {
        var mondayFirst = _settings.Get<int>(PreferenceKeys.WeekStart) != 0;
        return CalendarMonthModelBuilder.Build(year, month, mondayFirst);
    }

    public Task<IReadOnlyList<ScheduleEvent>> EventsOnAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var events = QueryExpanded(date.AddDays(-RecurringCoverageDays), date)
            .Where(item => item.StartDate <= date && (item.EndDate ?? item.StartDate) >= date)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ScheduleEvent>>(SortForDay(events, date));
    }

    public Task<IReadOnlyList<ScheduleEvent>> EventsBetweenAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        if (end < start)
        {
            return Task.FromResult<IReadOnlyList<ScheduleEvent>>(Array.Empty<ScheduleEvent>());
        }

        var events = QueryExpanded(start, end);

        return Task.FromResult<IReadOnlyList<ScheduleEvent>>(
            events
                .OrderBy(item => item.StartDate)
                .ThenBy(item => item.StartTime ?? TimeOnly.MinValue)
                .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                .ToArray());
    }

    /// <summary>合并单次记录与周期实例，按 (Id, StartDate) 去重。</summary>
    private IReadOnlyList<ScheduleEvent> QueryExpanded(DateOnly start, DateOnly end)
    {
        var singles = _schedules.QueryRange(start, end)
            .Where(item => item.Repeat is null);

        var instances = new List<ScheduleEvent>();

        foreach (var master in _schedules.QueryRecurring(end))
        {
            foreach (var instance in RepeatRuleEngine.Expand(master, start, end))
            {
                if (instance.StartDate <= end && (instance.EndDate ?? instance.StartDate) >= start)
                {
                    instances.Add(instance);
                }
            }
        }

        return singles
            .Concat(instances)
            .GroupBy(item => (item.Id, item.StartDate))
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// 当日排序：全天 → 跨天进行中 → 定时；同组按开始时间、优先级高→低、标题。
    /// </summary>
    private static IReadOnlyList<ScheduleEvent> SortForDay(
        IEnumerable<ScheduleEvent> events,
        DateOnly date)
    {
        return events
            .OrderBy(item => item.StartTime is null ? 0 : item.StartDate < date ? 1 : 2)
            .ThenBy(item => item.StartTime ?? TimeOnly.MinValue)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();
    }
}
