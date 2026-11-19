using Lumina.Domain.Models;

namespace Lumina.Services.Contracts;

/// <summary>单个世界时钟卡的显示快照；<see cref="CityId"/> 为 0 表示本地时间。</summary>
public sealed record WorldClockDisplay(
    long CityId,
    string CityName,
    string IanaId,
    DateTimeOffset Time,
    DateOnly Date,
    string? LunarText,
    bool IsLocal);

/// <summary>
/// P1-02 世界时钟：扁平城市列表、选中地区、显示快照与增删管理。
/// 地区选择入口只在设置中心；时钟页只展示，不出现地区下拉。
/// </summary>
public interface IWorldClockService
{
    IReadOnlyList<WorldCity> Cities();

    IReadOnlyList<WorldClockDisplay> AllDisplays();

    WorldClockDisplay SelectedDisplay();

    long SelectedCityId { get; }

    long AddCity(string ianaId, string displayName);

    void RemoveCity(long cityId);

    void SelectCity(long cityId);

    event Action? Changed;
}
