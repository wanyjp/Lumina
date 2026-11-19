using Lumina.Domain.Enums;

namespace Lumina.Domain.Models;

/// <summary>
/// 倒计时实体。锚点按重复方式分存，同一时刻只有对应的一种锚点有效。
/// 下次发生日计算与旧数据迁移在 P3-03 实现。
/// </summary>
public sealed record CountdownItem
{
    public long Id { get; init; }

    public string? LegacyId { get; init; }

    public required string Name { get; init; }

    public CountdownCategory Category { get; init; } = CountdownCategory.Other;

    public CountdownRepeat Repeat { get; init; } = CountdownRepeat.None;

    public ReminderOffset Reminder { get; init; } = ReminderOffset.Day1;

    /// <summary>不重复时的完整日期。</summary>
    public DateOnly? AnchorDate { get; init; }

    /// <summary>每周：0–6，0 表示周日。</summary>
    public int? AnchorWeekday { get; init; }

    /// <summary>每月日号 1–31，或每年日号。</summary>
    public int? AnchorDay { get; init; }

    /// <summary>每年月份 1–12。</summary>
    public int? AnchorMonth { get; init; }

    /// <summary>每月：true 表示月底。</summary>
    public bool AnchorMonthEnd { get; init; }

    /// <summary>旧字段，保留但不参与渲染。</summary>
    public string? LegacyColor { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
