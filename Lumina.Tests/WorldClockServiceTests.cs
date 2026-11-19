using Lumina.Data.Cities;
using Lumina.Data.Settings;
using Lumina.Services.Clock;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-02 世界时钟：扁平列表、选中持久化、删除回退、时区校验。</summary>
public class WorldClockServiceTests
{
    [Fact]
    public void Migration_seeds_six_preset_cities()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var store = new WorldCityRepository(temp.Database);
        var cities = store.GetAll();

        Assert.Equal(6, cities.Count);
        Assert.Equal("纽约", cities[0].DisplayName);
        Assert.Equal("America/New_York", cities[0].IanaId);
        Assert.Equal("迪拜", cities[^1].DisplayName);
        Assert.Equal("Asia/Dubai", cities[^1].IanaId);
    }

    [Fact]
    public void Flat_list_starts_with_local_time_then_presets()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var displays = service.AllDisplays();

        Assert.Equal(7, displays.Count);
        Assert.True(displays[0].IsLocal);
        Assert.StartsWith("本地时间", displays[0].CityName, StringComparison.Ordinal);
        Assert.Contains("UTC", displays[0].CityName, StringComparison.Ordinal);
        Assert.Equal("纽约", displays[1].CityName);
        Assert.All(displays.Skip(1), display => Assert.False(display.IsLocal));
    }

    [Fact]
    public void Selected_city_is_persisted_and_survives_reload()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);
        var cityId = service.Cities()[0].Id;

        service.SelectCity(cityId);
        Assert.Equal(cityId, service.SelectedCityId);

        var reloaded = CreateService(temp);
        Assert.Equal(cityId, reloaded.SelectedCityId);
        Assert.Equal("纽约", reloaded.SelectedDisplay().CityName);
    }

    [Fact]
    public void Removing_selected_city_falls_back_to_local()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);
        var cityId = service.Cities()[0].Id;

        service.SelectCity(cityId);
        service.RemoveCity(cityId);

        Assert.Equal(0, service.SelectedCityId);
        Assert.True(service.SelectedDisplay().IsLocal);
        Assert.Equal(5, service.Cities().Count);
    }

    [Fact]
    public void Adding_city_rejects_duplicate_time_zone_and_unknown_zone()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        Assert.Throws<InvalidOperationException>(
            () => service.AddCity("Asia/Tokyo", "东京"));
        Assert.Throws<ArgumentException>(
            () => service.AddCity("Mars/Olympus", "火星"));
    }

    [Fact]
    public void Added_city_appears_after_presets_and_can_be_removed()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var id = service.AddCity("Asia/Seoul", "首尔");
        var displays = service.AllDisplays();

        Assert.Equal(8, displays.Count);
        Assert.Equal("首尔", displays[^1].CityName);

        service.RemoveCity(id);
        Assert.Equal(7, service.AllDisplays().Count);
    }

    [Fact]
    public void City_times_respect_iana_zone_offsets()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var displays = service.AllDisplays();
        var newYork = displays.Single(display => display.CityName == "纽约");
        var tokyo = displays.Single(display => display.CityName == "东京");

        var newYorkOffset = newYork.Time.Offset;
        var tokyoOffset = tokyo.Time.Offset;

        Assert.NotEqual(newYorkOffset, tokyoOffset);
        Assert.Equal(newYork.Time.ToUniversalTime(), tokyo.Time.ToUniversalTime());
    }

    [Fact]
    public void Fixed_offset_zones_are_supported_for_regional_time()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var id = service.AddCity("UTC+06:00", "东六区");
        var display = service.AllDisplays().Single(item => item.CityId == id);

        Assert.Equal("东六区", display.CityName);
        Assert.Equal(TimeSpan.FromHours(6), display.Time.Offset);
        Assert.Equal(
            display.Time.ToUniversalTime(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("UTC+08:00", 8)]
    [InlineData("UTC+06:00", 6)]
    [InlineData("GMT-3", -3)]
    [InlineData("UTC+05:30", 5.5)]
    public void Fixed_offset_parser_accepts_common_forms(string id, double hours)
    {
        Assert.True(WorldClockTimeZoneResolver.TryParseFixedOffset(id, out var offset));
        Assert.Equal(TimeSpan.FromHours(hours), offset);
    }

    [Theory]
    [InlineData("Asia/Shanghai")]
    [InlineData("UTC")]
    [InlineData("UTC+15:00")]
    [InlineData("Mars/Olympus")]
    public void Fixed_offset_parser_rejects_non_offset_ids(string id)
    {
        Assert.False(WorldClockTimeZoneResolver.TryParseFixedOffset(id, out _));
    }

    [Fact]
    public void Catalog_covers_global_capitals_and_all_zones_resolve()
    {
        Assert.True(WorldCityCatalog.All.Count >= 100);

        Assert.All(
            WorldCityCatalog.All,
            option => Assert.True(
                WorldClockTimeZoneResolver.TryResolve(option.IanaId, out _),
                $"{option.DisplayName} 的时区 {option.IanaId} 无法解析"));

        Assert.Equal(
            WorldCityCatalog.All.Count,
            WorldCityCatalog.All
                .Select(option => option.IanaId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        var keys = WorldCityCatalog.All.Select(option => option.SortKey).ToArray();
        Assert.Equal(
            keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray(),
            keys);
    }

    private static WorldClockService CreateService(TemporaryDataRoot temp)
    {
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        return new WorldClockService(new WorldCityRepository(temp.Database), settings);
    }
}
