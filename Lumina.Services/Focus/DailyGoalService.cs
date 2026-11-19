using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Focus;

/// <summary>每日目标读写实现（P3-01）；数值钳制到 0–1440 分钟。</summary>
public sealed class DailyGoalService : IDailyGoalService
{
    private readonly IDailyGoalStore _store;

    public DailyGoalService(IDailyGoalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<int> GetTargetMinutesAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minutes = Math.Clamp(_store.GetTargetMinutes(date), 0, FocusAggregator.MaxDailyGoalMinutes);
        return Task.FromResult(minutes);
    }

    public Task SetTargetMinutesAsync(
        DateOnly date,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.SetTargetMinutes(date, Math.Clamp(minutes, 0, FocusAggregator.MaxDailyGoalMinutes));
        return Task.CompletedTask;
    }
}
