using Lumina.Domain.Enums;

namespace Lumina.Services.Focus;

/// <summary>
/// 统计周期的单一状态源（P3-02）：当前周期类型 + 偏移，驱动翻页器、标题、图表与导出。
/// 偏移 0 表示当前周期，负数向过去；不允许翻到未来。
/// </summary>
public sealed class PeriodNavigator
{
    public PeriodKind Kind { get; private set; } = PeriodKind.Week7;

    public int Offset { get; private set; }

    /// <summary>是否还能向更近的周期翻页。</summary>
    public bool CanGoNext => Offset < 0;

    /// <summary>历史月报不显示翻页器。</summary>
    public bool ShowsPager => Kind != PeriodKind.MonthlyArchive;

    /// <summary>切换周期类型并回到当前周期（重复切换同一类型也归零，保证状态可预期）。</summary>
    public void Switch(PeriodKind kind)
    {
        Kind = kind;
        Offset = 0;
    }

    /// <summary>历史回溯下限：约 10 年，避免偏移无限递减导致日期溢出。</summary>
    private const int MinimumOffset = -520;

    public bool MovePrevious()
    {
        if (Offset <= MinimumOffset)
        {
            return false;
        }

        Offset--;
        return true;
    }

    public bool MoveNext()
    {
        if (!CanGoNext)
        {
            return false;
        }

        Offset++;
        return true;
    }

    /// <summary>直接跳到某个偏移（归档月份选择用）；不允许未来，负数向过去。</summary>
    public void SetOffset(int offset) => Offset = Math.Clamp(offset, MinimumOffset, 0);

    public FocusPeriod Period(DateTimeOffset now) => FocusAggregator.Resolve(Kind, Offset, now);

    /// <summary>相对标签：本周 / 上周 / 2 周前；最近 30 天 / 上一个 30 天；历史月报为「2026 年 10 月」。</summary>
    public string Label(DateTimeOffset now)
    {
        var period = Period(now);

        if (Kind == PeriodKind.MonthlyArchive)
        {
            return $"{period.Start.Year} 年 {period.Start.Month} 月";
        }

        if (Offset == 0)
        {
            return Kind == PeriodKind.Week7 ? "本周" : "最近 30 天";
        }

        var weeks = -Offset;
        return Kind == PeriodKind.Week7
            ? weeks == 1 ? "上周" : $"{weeks} 周前"
            : weeks == 1 ? "上一个 30 天" : $"{weeks} 个 30 天前";
    }

    /// <summary>起止日期文本，含首含尾；跨年时补全年份。</summary>
    public string RangeText(DateTimeOffset now)
    {
        var period = Period(now);
        return period.Start.Year == period.End.Year
            ? $"{period.Start:MM-dd} ~ {period.End:MM-dd}"
            : $"{period.Start:yyyy-MM-dd} ~ {period.End:yyyy-MM-dd}";
    }
}
