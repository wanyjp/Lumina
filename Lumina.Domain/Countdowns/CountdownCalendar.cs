using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Domain.Countdowns;

/// <summary>
/// 倒计时日期与重复滚动纯逻辑（P3-03）。
/// 口径：每月短月取当月最后一天；月底锚点恒取当月最后一天；每年 2/29 在平年取 2/28；
/// 每周当天命中即今天；重复项的下次发生日恒不早于参考日，剩余天数永不为负；
/// 所有天数均为自然日差（<see cref="DateOnly.DayNumber"/> 相减），不用小时差取整。
/// </summary>
public static class CountdownCalendar
{
    /// <summary>每月锚点的最大向前搜索月数；正常最多 2 个月即可命中。</summary>
    public const int MonthlySearchMonths = 24;

    /// <summary>每年锚点的最大向前搜索年数；2/29 在平年收敛后每年均可命中。</summary>
    public const int YearlySearchYears = 8;

    /// <summary>不重复项的进度条折算窗口；与原型保持一致。</summary>
    public const int NonRepeatingWindowDays = 365;

    /// <summary>
    /// 解析倒计时在 <paramref name="today"/> 的状态：下次（或原）发生日、自然日差与是否已过期。
    /// 锚点缺失或非法时返回 <see cref="CountdownOccurrence.Invalid"/>，不抛异常。
    /// </summary>
    public static CountdownOccurrence Resolve(CountdownItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Repeat switch
        {
            CountdownRepeat.None => ResolveOnce(item, today),
            CountdownRepeat.Weekly => ResolveWeekly(item, today),
            CountdownRepeat.Monthly => ResolveMonthly(item, today),
            CountdownRepeat.Yearly => ResolveYearly(item, today),
            _ => CountdownOccurrence.Invalid,
        };
    }

    /// <summary>
    /// 提醒与组件使用的「下一次发生日」：不重复项只在日期未过时返回，重复项滚动到不早于
    /// <paramref name="from"/> 的最近一次。
    /// </summary>
    public static DateOnly? NextOccurrence(CountdownItem item, DateOnly from)
    {
        var occurrence = Resolve(item, from);
        if (!occurrence.IsValid || occurrence.Date is not { } date)
        {
            return null;
        }

        // 不重复项已过期：没有下一次，提醒不再触发。
        return occurrence.IsExpired ? null : date;
    }

    /// <summary>
    /// 紧迫度快照：五档阈值 + 自然日差 + 进度条比例 + 是否呼吸。
    /// 颜色与动效由表现层消费；系统「减少动画」时表现层自行关闭呼吸。
    /// </summary>
    public static UrgencySnapshot Evaluate(CountdownItem item, DateOnly today)
    {
        var occurrence = Resolve(item, today);
        if (!occurrence.IsValid || occurrence.Date is null)
        {
            return new UrgencySnapshot
            {
                Level = UrgencyLevel.Calm,
                DaysRemaining = 0,
                ProgressRatio = 0,
                ShouldBreathe = false,
                NextOccurrence = null,
            };
        }

        var level = occurrence.IsExpired
            ? UrgencyLevel.Expired
            : LevelFor(occurrence.DaysRemaining);

        return new UrgencySnapshot
        {
            Level = level,
            DaysRemaining = occurrence.DaysRemaining,
            ProgressRatio = ProgressFor(item.Repeat, occurrence.DaysRemaining),
            ShouldBreathe = level == UrgencyLevel.Urgent,
            NextOccurrence = occurrence.Date,
        };
    }

    /// <summary>未过期时的五档阈值：≤3 紧迫、≤7 临近、≤30 渐近，其余从容。</summary>
    public static UrgencyLevel LevelFor(int daysRemaining) => daysRemaining switch
    {
        <= 3 => UrgencyLevel.Urgent,
        <= 7 => UrgencyLevel.Hot,
        <= 30 => UrgencyLevel.Warm,
        _ => UrgencyLevel.Calm,
    };

    /// <summary>
    /// 按重复窗口折算进度：当天 100%，其余按剩余天数占窗口比例递减，钳制在 8%–96%；
    /// 不重复项使用 365 天窗口（与原型一致）。
    /// </summary>
    public static double ProgressFor(CountdownRepeat repeat, int daysRemaining)
    {
        if (daysRemaining == 0)
        {
            return 1d;
        }

        var window = repeat switch
        {
            CountdownRepeat.Weekly => 7,
            CountdownRepeat.Monthly => 31,
            CountdownRepeat.Yearly => 365,
            _ => NonRepeatingWindowDays,
        };

        var consumed = Math.Min(Math.Abs(daysRemaining), window) / (double)window;
        return Math.Clamp(1d - consumed, 0.08d, 0.96d);
    }

    /// <summary>
    /// 按当前 <see cref="CountdownItem.Repeat"/> 把参考日期写入对应锚点，并清空其余锚点。
    /// 每月 <paramref name="monthEnd"/> 为 true 时只存月底标记，不再保存日号。
    /// </summary>
    public static CountdownItem WithAnchor(CountdownItem item, DateOnly date, bool monthEnd = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Repeat switch
        {
            CountdownRepeat.Weekly => item with
            {
                AnchorDate = null,
                AnchorWeekday = (int)date.DayOfWeek,
                AnchorDay = null,
                AnchorMonth = null,
                AnchorMonthEnd = false,
            },
            CountdownRepeat.Monthly => item with
            {
                AnchorDate = null,
                AnchorWeekday = null,
                AnchorDay = monthEnd ? null : date.Day,
                AnchorMonth = null,
                AnchorMonthEnd = monthEnd,
            },
            CountdownRepeat.Yearly => item with
            {
                AnchorDate = null,
                AnchorWeekday = null,
                AnchorDay = date.Day,
                AnchorMonth = date.Month,
                AnchorMonthEnd = false,
            },
            _ => item with
            {
                AnchorDate = date,
                AnchorWeekday = null,
                AnchorDay = null,
                AnchorMonth = null,
                AnchorMonthEnd = false,
            },
        };
    }

    /// <summary>
    /// 切换重复方式：只保留新方式需要的锚点，旧锚点一律清空，避免刷新后旧值复活。
    /// 重复方式未变时原样返回，便于编辑态保留用户已选锚点。
    /// </summary>
    public static CountdownItem WithRepeat(CountdownItem item, CountdownRepeat repeat)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Repeat == repeat ? item : ClearAnchors(item) with { Repeat = repeat };
    }

    /// <summary>清空全部锚点，保留名称、分类、重复方式与提醒档位。</summary>
    public static CountdownItem ClearAnchors(CountdownItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item with
        {
            AnchorDate = null,
            AnchorWeekday = null,
            AnchorDay = null,
            AnchorMonth = null,
            AnchorMonthEnd = false,
        };
    }

    private static CountdownOccurrence ResolveOnce(CountdownItem item, DateOnly today)
    {
        if (item.AnchorDate is not { } date)
        {
            return CountdownOccurrence.Invalid;
        }

        var days = date.DayNumber - today.DayNumber;
        return new CountdownOccurrence
        {
            IsValid = true,
            Date = date,
            DaysRemaining = days,
            IsExpired = days < 0,
        };
    }

    private static CountdownOccurrence ResolveWeekly(CountdownItem item, DateOnly today)
    {
        if (item.AnchorWeekday is not { } weekday || weekday is < 0 or > 6)
        {
            return CountdownOccurrence.Invalid;
        }

        // 当天命中即今天：偏移量 0。
        var offset = ((weekday - (int)today.DayOfWeek) + 7) % 7;
        return new CountdownOccurrence
        {
            IsValid = true,
            Date = today.AddDays(offset),
            DaysRemaining = offset,
            IsExpired = false,
        };
    }

    private static CountdownOccurrence ResolveMonthly(CountdownItem item, DateOnly today)
    {
        if (!item.AnchorMonthEnd
            && (item.AnchorDay is not { } anchor || anchor is < 1 or > 31))
        {
            return CountdownOccurrence.Invalid;
        }

        var month = new DateOnly(today.Year, today.Month, 1);
        for (var offset = 0; offset < MonthlySearchMonths; offset++)
        {
            var cursor = month.AddMonths(offset);
            var daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);

            // 月底锚点取最后一天；短月（如 31 号遇到 2 月）同样收敛到最后一天。
            var day = item.AnchorMonthEnd
                ? daysInMonth
                : Math.Min(item.AnchorDay!.Value, daysInMonth);
            var date = new DateOnly(cursor.Year, cursor.Month, day);

            if (date >= today)
            {
                return new CountdownOccurrence
                {
                    IsValid = true,
                    Date = date,
                    DaysRemaining = date.DayNumber - today.DayNumber,
                    IsExpired = false,
                };
            }
        }

        return CountdownOccurrence.Invalid;
    }

    private static CountdownOccurrence ResolveYearly(CountdownItem item, DateOnly today)
    {
        if (item.AnchorMonth is not { } month || month is < 1 or > 12)
        {
            return CountdownOccurrence.Invalid;
        }

        if (item.AnchorDay is not { } day || day is < 1 or > 31)
        {
            return CountdownOccurrence.Invalid;
        }

        // 用闰年 2000 校验「月份 + 日号」本身是否合法（例如 2/30 不允许，2/29 允许）。
        if (day > DateTime.DaysInMonth(2000, month))
        {
            return CountdownOccurrence.Invalid;
        }

        for (var offset = 0; offset < YearlySearchYears; offset++)
        {
            var year = today.Year + offset;
            var daysInMonth = DateTime.DaysInMonth(year, month);

            // 2/29 在平年收敛到 2/28。
            var effectiveDay = Math.Min(day, daysInMonth);
            var date = new DateOnly(year, month, effectiveDay);

            if (date >= today)
            {
                return new CountdownOccurrence
                {
                    IsValid = true,
                    Date = date,
                    DaysRemaining = date.DayNumber - today.DayNumber,
                    IsExpired = false,
                };
            }
        }

        return CountdownOccurrence.Invalid;
    }
}
