using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clock;

/// <summary>
/// P1-02 世界时钟服务：本地时间 → 预设 → 自建的扁平列表；选中地区持久化，
/// 删除选中城市立即回退本地。时区与 DST 全部交给 <see cref="TimeZoneInfo"/>。
/// </summary>
public sealed class WorldClockService : IWorldClockService
{
    private readonly IWorldCityStore _cityStore;
    private readonly ISettingsStore _settings;
    private long _selectedCityId;

    public WorldClockService(IWorldCityStore cityStore, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(cityStore);
        ArgumentNullException.ThrowIfNull(settings);

        _cityStore = cityStore;
        _settings = settings;
        _selectedCityId = settings.Get<long>(
            PreferenceKeys.WorldSelectedCity,
            PreferenceDefaults.WorldSelectedCity);

        if (_selectedCityId != 0 && !CityExists(_selectedCityId))
        {
            _selectedCityId = 0;
        }
    }

    public event Action? Changed;

    public long SelectedCityId => _selectedCityId;

    public IReadOnlyList<WorldCity> Cities() => _cityStore.GetAll();

    public IReadOnlyList<WorldClockDisplay> AllDisplays()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var displays = new List<WorldClockDisplay> { BuildLocal(DateTimeOffset.Now) };

        foreach (var city in _cityStore.GetAll())
        {
            var display = TryBuildCity(city, utcNow);
            if (display is not null)
            {
                displays.Add(display);
            }
        }

        return displays;
    }

    public WorldClockDisplay SelectedDisplay()
    {
        var displays = AllDisplays();
        foreach (var display in displays)
        {
            if (display.CityId == _selectedCityId)
            {
                return display;
            }
        }

        return displays[0];
    }

    public long AddCity(string ianaId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ianaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var name = displayName.Trim();
        if (name.Length > 32)
        {
            throw new ArgumentException("城市显示名称最多 32 个字符。", nameof(displayName));
        }

        EnsureValidTimeZone(ianaId);

        var cities = _cityStore.GetAll();
        if (cities.Any(city => string.Equals(city.IanaId, ianaId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该城市已添加。");
        }

        var sortOrder = cities.Count == 0 ? 1 : cities.Max(city => city.SortOrder) + 1;
        var id = _cityStore.Add(ianaId, name, sortOrder);
        Changed?.Invoke();
        return id;
    }

    public void RemoveCity(long cityId)
    {
        _cityStore.Remove(cityId);

        if (_selectedCityId == cityId)
        {
            _selectedCityId = 0;
            _settings.Set(PreferenceKeys.WorldSelectedCity, 0L, module: "clock");
        }

        Changed?.Invoke();
    }

    public void SelectCity(long cityId)
    {
        if (cityId != 0 && !CityExists(cityId))
        {
            cityId = 0;
        }

        _selectedCityId = cityId;
        _settings.Set(PreferenceKeys.WorldSelectedCity, cityId, module: "clock");
        Changed?.Invoke();
    }

    private bool CityExists(long cityId) =>
        _cityStore.GetAll().Any(city => city.Id == cityId);

    private static WorldClockDisplay BuildLocal(DateTimeOffset local)
    {
        var date = DateOnly.FromDateTime(local.DateTime);
        var offsetText = WorldClockTimeZoneResolver.DescribeOffset(TimeZoneInfo.Local, local);
        return new WorldClockDisplay(
            CityId: 0,
            CityName: $"本地时间 · {offsetText}",
            IanaId: "__local",
            Time: local,
            Date: date,
            LunarText: ClockTextFormatter.LunarText(date),
            IsLocal: true);
    }

    private static WorldClockDisplay? TryBuildCity(WorldCity city, DateTimeOffset utcNow)
    {
        try
        {
            var timeZone = WorldClockTimeZoneResolver.Resolve(city.IanaId);
            var cityTime = TimeZoneInfo.ConvertTime(utcNow, timeZone);
            var date = DateOnly.FromDateTime(cityTime.DateTime);

            return new WorldClockDisplay(
                city.Id,
                city.DisplayName,
                city.IanaId,
                cityTime,
                date,
                ClockTextFormatter.LunarText(date),
                IsLocal: false);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException
                                       or InvalidTimeZoneException
                                       or ArgumentException)
        {
            return null;
        }
    }

    private static void EnsureValidTimeZone(string ianaId)
    {
        try
        {
            WorldClockTimeZoneResolver.Resolve(ianaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException
                                       or InvalidTimeZoneException
                                       or ArgumentException)
        {
            throw new ArgumentException($"无效的时区：{ianaId}", nameof(ianaId), ex);
        }
    }
}
