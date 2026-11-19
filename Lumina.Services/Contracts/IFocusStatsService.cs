using Lumina.Domain.Enums;

namespace Lumina.Services.Contracts;

/// <summary>
/// 专注统计视图的数据入口（P3-02）。一次返回本期聚合、上期聚合、逐日序列与今日目标，
/// 保证翻页器、图表、标题与导出都基于同一份快照。
/// </summary>
public interface IFocusStatsService
{
    Task<FocusStatsSnapshot> LoadAsync(
        PeriodKind kind,
        int offset,
        CancellationToken cancellationToken = default);
}
