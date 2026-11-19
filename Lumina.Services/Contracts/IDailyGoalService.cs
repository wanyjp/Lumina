namespace Lumina.Services.Contracts;

/// <summary>
/// 每日专注目标（P3-01）。目标按本地自然日存储，单位分钟；0 表示未设定。
/// P3-02 统计页读取并按目标计算达成率。
/// </summary>
public interface IDailyGoalService
{
    Task<int> GetTargetMinutesAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>设置某日目标；入参会被钳制到 0–1440 分钟。</summary>
    Task SetTargetMinutesAsync(
        DateOnly date,
        int minutes,
        CancellationToken cancellationToken = default);
}
