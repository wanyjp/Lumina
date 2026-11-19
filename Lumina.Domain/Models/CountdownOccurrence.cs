namespace Lumina.Domain.Models;

/// <summary>
/// 倒计时在某个参考日的解析结果。`IsValid=false` 表示锚点缺失或非法（UI 显示「日期无效」）；
/// `IsExpired=true` 只可能出现在不重复项，重复项总会滚动到下一周期。
/// </summary>
public sealed record CountdownOccurrence
{
    public static CountdownOccurrence Invalid { get; } = new()
    {
        IsValid = false,
        Date = null,
        DaysRemaining = 0,
        IsExpired = false,
    };

    public required bool IsValid { get; init; }

    /// <summary>显示用的目标日期；不重复项已过期时仍返回原日期。</summary>
    public DateOnly? Date { get; init; }

    /// <summary>自然日差；重复项永不为负，不重复项可为负。</summary>
    public required int DaysRemaining { get; init; }

    public required bool IsExpired { get; init; }
}
