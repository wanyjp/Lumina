namespace Lumina.Domain.Stores;

/// <summary>每日目标存储（P3-01）。按本地自然日存分钟数，缺省为 0（未设定）。</summary>
public interface IDailyGoalStore
{
    int GetTargetMinutes(DateOnly date);

    void SetTargetMinutes(DateOnly date, int minutes);
}
