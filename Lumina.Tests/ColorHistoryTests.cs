using Lumina.Data.Settings;
using Lumina.Services.Colors;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-05 历史色板：去重、上限与持久化。</summary>
public class ColorHistoryTests
{
    [Fact]
    public async Task History_dedupes_and_keeps_most_recent_first()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var history = new ColorHistoryService(store);

        await history.AddAsync(new ColorSample(1, 2, 3, 10, 20), TestContext.Current.CancellationToken);
        await history.AddAsync(new ColorSample(4, 5, 6, 30, 40), TestContext.Current.CancellationToken);
        var colors = await history.AddAsync(new ColorSample(1, 2, 3, 50, 60), TestContext.Current.CancellationToken);

        Assert.Equal(2, colors.Count);
        Assert.Equal("#010203", colors[0].Hex);
        Assert.Equal("#040506", colors[1].Hex);
    }

    [Fact]
    public async Task History_is_capped_and_survives_reload()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var databasePath = temp.Database;
        var store = new SettingsStore(new SqliteSettingsBackend(databasePath));
        var history = new ColorHistoryService(store);

        for (var index = 0; index < ColorHistoryService.Limit + 5; index++)
        {
            await history.AddAsync(
                new ColorSample((byte)index, (byte)(index + 1), (byte)(index + 2), 0, 0),
                TestContext.Current.CancellationToken);
        }

        var colors = history.Load();
        Assert.Equal(ColorHistoryService.Limit, colors.Count);

        var reloaded = new ColorHistoryService(new SettingsStore(new SqliteSettingsBackend(databasePath)));
        var persisted = reloaded.Load();
        Assert.Equal(ColorHistoryService.Limit, persisted.Count);
        Assert.Equal(colors[0].Hex, persisted[0].Hex);
    }

    [Fact]
    public void Corrupted_history_falls_back_to_empty()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        store.Set(PreferenceKeys.ColorHistory, "not-json");

        var history = new ColorHistoryService(store);

        Assert.Empty(history.Load());
    }
}
