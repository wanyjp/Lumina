using Lumina.Domain.Enums;

namespace Lumina.Domain.Models;

/// <summary>
/// 周期规则骨架。字段语义与工程方案 4.2 一致；规则引擎在 P1-07 实现，本阶段只冻结字段。
/// </summary>
public sealed record RepeatRule
{
    public required RepeatMode Mode { get; init; }

    public RepeatEndType EndType { get; init; } = RepeatEndType.Date;

    public DateOnly? EndDate { get; init; }

    public int? Count { get; init; }

    public int? WeekStart { get; init; }

    public int? WeekEnd { get; init; }

    public int? MonthStart { get; init; }

    /// <summary>1–31 或字符串 `end` 表示月底。</summary>
    public string? MonthEnd { get; init; }

    /// <summary>0–6，0 表示周日；至少 1 个。</summary>
    public IReadOnlyList<int>? Weekdays { get; init; }

    /// <summary>1–31 或 `end`。</summary>
    public string? MonthDay { get; init; }
}

/// <summary>日程实体。`StartTime` 为空表示全天；`EndTime` 为空且 `StartTime` 存在表示开放结束。</summary>
public sealed record ScheduleEvent
{
    public long Id { get; init; }

    public string? LegacyId { get; init; }

    public required string Title { get; init; }

    public required DateOnly StartDate { get; init; }

    public TimeOnly? StartTime { get; init; }

    public DateOnly? EndDate { get; init; }

    public TimeOnly? EndTime { get; init; }

    public string Location { get; init; } = string.Empty;

    public string? Note { get; init; }

    public Priority Priority { get; init; } = Priority.Normal;

    public RepeatRule? Repeat { get; init; }

    public ReminderOffset Reminder { get; init; } = ReminderOffset.None;

    public bool Done { get; init; }
}

/// <summary>待办实体；`Text` 为标题，保存前需 trim。</summary>
public sealed record TodoItem
{
    public long Id { get; init; }

    public string? LegacyId { get; init; }

    public required string Text { get; init; }

    public DateOnly? DueDate { get; init; }

    public TimeOnly? DueTime { get; init; }

    public Priority Priority { get; init; } = Priority.Normal;

    public bool Done { get; init; }

    public DateTimeOffset? DoneAt { get; init; }

    public string Note { get; init; } = string.Empty;

    /// <summary>由便签转换而来时记录来源便签 Id（P3-06 回链，重复点击不重复创建）。</summary>
    public long? NoteId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>待办筛选条件；由 P1-09 实现查询。</summary>
public sealed record TodoFilter
{
    public TodoStatus Status { get; init; } = TodoStatus.All;

    public Priority? Priority { get; init; }

    public TodoDateRange DateRange { get; init; } = TodoDateRange.All;

    public TodoSort Sort { get; init; } = TodoSort.Smart;
}
