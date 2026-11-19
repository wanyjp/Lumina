using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Countdowns;

/// <summary>
/// P3-03 倒计时服务：名称/分类/重复/提醒归一化，锚点按重复方式单存，
/// 下次发生日与紧迫度统一走 <see cref="CountdownCalendar"/>。
/// 新增时 Id=0；更新时保留创建时间；切换重复方式由编辑侧调用
/// <see cref="CountdownCalendar.WithRepeat"/> 清空旧锚点，服务只落库当前方式的锚点。
/// </summary>
public sealed class CountdownService : ICountdownService
{
    private readonly ICountdownStore _store;
    private readonly Func<DateTimeOffset> _now;

    public CountdownService(ICountdownStore store, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action? Changed;

    public IReadOnlyList<CountdownItem> All()
    {
        var today = Today();

        // 未过期在前、剩余天数升序；已过期在后、最近过期优先；日期无效最后。
        return _store.QueryAll()
            .Select(item => (Item: item, Occurrence: CountdownCalendar.Resolve(item, today)))
            .OrderBy(entry => entry.Occurrence.IsValid ? 0 : 2)
            .ThenBy(entry => entry.Occurrence.IsExpired ? 1 : 0)
            .ThenBy(entry => entry.Occurrence.IsExpired
                ? -entry.Occurrence.DaysRemaining
                : entry.Occurrence.DaysRemaining)
            .ThenBy(entry => entry.Item.Id)
            .Select(entry => entry.Item)
            .ToArray();
    }

    public UrgencySnapshot Evaluate(CountdownItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return CountdownCalendar.Evaluate(item, Today());
    }

    public Task<CountdownItem> AddOrUpdateAsync(
        CountdownItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var now = _now();
        CountdownItem? existing = null;
        if (item.Id > 0)
        {
            existing = _store.Find(item.Id)
                ?? throw new ArgumentException("倒计时不存在。", nameof(item));
        }

        // 切换重复方式时由编辑侧调用 CountdownCalendar.WithRepeat 清空旧锚点；
        // 服务侧归一化只保留当前重复方式对应的锚点，跨类型字段不会落库。
        var normalized = Normalize(item);

        if (existing is not null)
        {
            var updated = normalized with
            {
                LegacyId = normalized.LegacyId ?? existing.LegacyId,
                LegacyColor = normalized.LegacyColor ?? existing.LegacyColor,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = now,
            };
            _store.Update(updated, now);
            Changed?.Invoke();
            return Task.FromResult(updated);
        }

        var created = normalized with { CreatedAt = now, UpdatedAt = now };
        var id = _store.Insert(created, now);
        created = created with { Id = id };
        Changed?.Invoke();
        return Task.FromResult(created);
    }

    public Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _store.Delete(ids);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 归一化：名称去空白、非法枚举回落默认值；锚点缺失时按旧 <see cref="CountdownItem.AnchorDate"/>
    /// 反推（兼容迁移前的旧数据），并按当前重复方式清空其余锚点。
    /// </summary>
    internal static CountdownItem Normalize(CountdownItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var name = item.Name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("倒计时名称不能为空。", nameof(item));
        }

        var normalized = item with
        {
            Name = name,
            Category = Enum.IsDefined(item.Category) ? item.Category : CountdownCategory.Other,
            Reminder = Enum.IsDefined(item.Reminder) ? item.Reminder : ReminderOffset.Day1,
        };

        return normalized.Repeat switch
        {
            CountdownRepeat.None => NormalizeOnce(normalized),
            CountdownRepeat.Weekly => NormalizeWeekly(normalized),
            CountdownRepeat.Monthly => NormalizeMonthly(normalized),
            CountdownRepeat.Yearly => NormalizeYearly(normalized),
            _ => throw new ArgumentException("未知的重复方式。", nameof(item)),
        };
    }

    private static CountdownItem NormalizeOnce(CountdownItem item)
    {
        var date = item.AnchorDate
            ?? throw new ArgumentException("不重复的倒计时需要目标日期。", nameof(item));

        return item with
        {
            AnchorDate = date,
            AnchorWeekday = null,
            AnchorDay = null,
            AnchorMonth = null,
            AnchorMonthEnd = false,
        };
    }

    private static CountdownItem NormalizeWeekly(CountdownItem item)
    {
        var weekday = item.AnchorWeekday;
        if (weekday is not { } value || value is < 0 or > 6)
        {
            if (item.AnchorDate is not { } date)
            {
                throw new ArgumentException("每周重复需要选择星期。", nameof(item));
            }

            value = (int)date.DayOfWeek;
        }

        return item with
        {
            AnchorDate = null,
            AnchorWeekday = value,
            AnchorDay = null,
            AnchorMonth = null,
            AnchorMonthEnd = false,
        };
    }

    private static CountdownItem NormalizeMonthly(CountdownItem item)
    {
        if (item.AnchorMonthEnd)
        {
            return item with
            {
                AnchorDate = null,
                AnchorWeekday = null,
                AnchorDay = null,
                AnchorMonth = null,
                AnchorMonthEnd = true,
            };
        }

        var day = item.AnchorDay;
        if (day is not { } value || value is < 1 or > 31)
        {
            if (item.AnchorDate is not { } date)
            {
                throw new ArgumentException("每月重复需要选择日号或月底。", nameof(item));
            }

            value = date.Day;
        }

        return item with
        {
            AnchorDate = null,
            AnchorWeekday = null,
            AnchorDay = value,
            AnchorMonth = null,
            AnchorMonthEnd = false,
        };
    }

    private static CountdownItem NormalizeYearly(CountdownItem item)
    {
        var month = item.AnchorMonth;
        var day = item.AnchorDay;

        if (month is not { } monthValue || monthValue is < 1 or > 12
            || day is not { } dayValue || dayValue is < 1 or > 31)
        {
            if (item.AnchorDate is not { } date)
            {
                throw new ArgumentException("每年重复需要选择月份与日号。", nameof(item));
            }

            monthValue = date.Month;
            dayValue = date.Day;
        }

        // 用闰年 2000 校验「月份 + 日号」：2/29 合法，2/30 等非法日期直接拒绝。
        if (dayValue > DateTime.DaysInMonth(2000, monthValue))
        {
            throw new ArgumentException("日期在所选月份中不存在。", nameof(item));
        }

        return item with
        {
            AnchorDate = null,
            AnchorWeekday = null,
            AnchorDay = dayValue,
            AnchorMonth = monthValue,
            AnchorMonthEnd = false,
        };
    }

    private DateOnly Today() => DateOnly.FromDateTime(_now().LocalDateTime);
}
