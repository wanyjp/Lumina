namespace Lumina.Domain.Enums;

/// <summary>待办与日程的优先级，对应数据库 `high` / `normal` / `low`。</summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>提前提醒七档，对应原型 `none/ontime/m5/m15/m30/h1/d1`。</summary>
public enum ReminderOffset
{
    None = 0,
    OnTime = 1,
    Minutes5 = 2,
    Minutes15 = 3,
    Minutes30 = 4,
    Hour1 = 5,
    Day1 = 6,
}

public static class ReminderOffsetExtensions
{
    /// <summary>把提醒档位换算为提前量；`None` 返回 <see cref="TimeSpan.Zero"/>。</summary>
    public static TimeSpan ToLeadTime(this ReminderOffset offset) => offset switch
    {
        ReminderOffset.OnTime => TimeSpan.Zero,
        ReminderOffset.Minutes5 => TimeSpan.FromMinutes(5),
        ReminderOffset.Minutes15 => TimeSpan.FromMinutes(15),
        ReminderOffset.Minutes30 => TimeSpan.FromMinutes(30),
        ReminderOffset.Hour1 => TimeSpan.FromHours(1),
        ReminderOffset.Day1 => TimeSpan.FromDays(1),
        _ => TimeSpan.Zero,
    };

    /// <summary>数据库与配置使用的稳定键名。</summary>
    public static string ToKey(this ReminderOffset offset) => offset switch
    {
        ReminderOffset.OnTime => "ontime",
        ReminderOffset.Minutes5 => "m5",
        ReminderOffset.Minutes15 => "m15",
        ReminderOffset.Minutes30 => "m30",
        ReminderOffset.Hour1 => "h1",
        ReminderOffset.Day1 => "d1",
        _ => "none",
    };
}

/// <summary>周期方式，共 7 种；对应原型 `week/month/weekdays/monthday/daily/workday/holiday`。</summary>
public enum RepeatMode
{
    Week = 0,
    Month = 1,
    Weekdays = 2,
    MonthDay = 3,
    Daily = 4,
    Workday = 5,
    Holiday = 6,
}

/// <summary>周期结束条件，共 3 种；不提供 `once`。</summary>
public enum RepeatEndType
{
    Date = 0,
    Count = 1,
    Forever = 2,
}

/// <summary>待办状态筛选。</summary>
public enum TodoStatus
{
    All = 0,
    Active = 1,
    Completed = 2,
    Overdue = 3,
}

/// <summary>待办截止日期筛选。</summary>
public enum TodoDateRange
{
    All = 0,
    Today = 1,
    ThisWeek = 2,
    Overdue = 3,
    NoDueDate = 4,
}

/// <summary>待办排序方式。</summary>
public enum TodoSort
{
    Smart = 0,
    DueDate = 1,
    Priority = 2,
    CreatedAt = 3,
}
