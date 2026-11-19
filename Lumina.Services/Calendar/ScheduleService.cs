using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Calendar;

/// <summary>
/// P1-06 日程 CRUD：标题 trim、起止日期与时间校验、跨天与空时间语义归一化。
/// 周期规则只原样保存，展开与边界校验在 P1-07 完成。
/// </summary>
public sealed class ScheduleService : IScheduleService
{
    private readonly IScheduleStore _store;
    private readonly Func<DateTimeOffset> _now;

    public ScheduleService(IScheduleStore store, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action? Changed;

    public Task<ScheduleEvent> AddAsync(ScheduleEvent item, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(item);
        var created = normalized with { Id = _store.Insert(normalized, _now()) };
        Changed?.Invoke();
        return Task.FromResult(created);
    }

    public Task UpdateAsync(ScheduleEvent item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Id <= 0)
        {
            throw new ArgumentException("更新日程需要有效的 id。", nameof(item));
        }

        _store.Update(Normalize(item), _now());
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _store.Delete(ids);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    internal static ScheduleEvent Normalize(ScheduleEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var title = item.Title.Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("日程标题不能为空。", nameof(item));
        }

        if (item.Repeat is not null)
        {
            if (item.EndDate is not null)
            {
                throw new ArgumentException("周期日程不支持跨天，请关闭周期后再设置结束日期。", nameof(item));
            }

            RepeatRuleValidator.Validate(item.Repeat, item.StartDate);
        }

        var endDate = item.EndDate;
        if (endDate is { } end && end < item.StartDate)
        {
            throw new ArgumentException("结束日期不能早于开始日期。", nameof(item));
        }

        if (item.StartTime is null && item.EndTime is not null)
        {
            throw new ArgumentException("设置结束时间前请先设置开始时间。", nameof(item));
        }

        if (item.StartTime is { } startTime && item.EndTime is { } finishTime)
        {
            var sameDay = endDate is null || endDate == item.StartDate;
            if (sameDay && finishTime < startTime)
            {
                throw new ArgumentException("同一天的结束时间不能早于开始时间。", nameof(item));
            }
        }

        var note = item.Note?.Trim();

        return item with
        {
            Title = title,
            Location = item.Location.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
        };
    }
}
