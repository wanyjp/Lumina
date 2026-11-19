using Lumina.Data.Settings;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>配置中枢的 SQLite / JSON 双后端与变更通知。</summary>
public class SettingsStoreTests
{
    [Fact]
    public void Sqlite_backend_roundtrips_values_and_falls_back_to_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));

        Assert.Equal(PreferenceDefaults.Theme, store.Get<string>(PreferenceKeys.Theme));
        Assert.Equal(PreferenceDefaults.AudioVolume, store.Get<int>(PreferenceKeys.AudioVolume));
        Assert.Equal("explicit", store.Get(PreferenceKeys.Theme, "explicit"));

        store.Set(PreferenceKeys.Theme, "dark");
        store.Set(PreferenceKeys.AudioVolume, 80);

        Assert.Equal("dark", store.Get<string>(PreferenceKeys.Theme));
        Assert.Equal(80, store.Get<int>(PreferenceKeys.AudioVolume));

        var reloaded = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        Assert.Equal("dark", reloaded.Get<string>(PreferenceKeys.Theme));
        Assert.Equal(80, reloaded.Get<int>(PreferenceKeys.AudioVolume));
    }

    [Fact]
    public void Settings_store_publishes_change_events()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var observer = new RecordingObserver<SettingChanged>();
        var subscription = store.Changed.Subscribe(observer);

        store.Set(PreferenceKeys.AudioVolume, 80);

        var changes = observer.Values;
        Assert.Single(changes);
        Assert.Equal(PreferenceKeys.AudioVolume, changes[0].Key);
        Assert.Equal(80, changes[0].Value);

        subscription.Dispose();
        store.Set(PreferenceKeys.AudioVolume, 90);
        Assert.Single(observer.Values);
    }

    [Fact]
    public async Task Json_backend_persists_to_settings_file()
    {
        using var temp = new TemporaryDataRoot();
        var filePath = Path.Combine(temp.ConfigDirectory, "settings.json");

        var store = new SettingsStore(new JsonSettingsBackend(filePath));
        store.Set(PreferenceKeys.Theme, "neon");
        Assert.Equal("neon", store.Get<string>(PreferenceKeys.Theme));

        await store.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(filePath));
        var json = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        Assert.Contains("\"theme\"", json);
        Assert.Contains("neon", json);

        var reloaded = new SettingsStore(new JsonSettingsBackend(filePath));
        Assert.Equal("neon", reloaded.Get<string>(PreferenceKeys.Theme));
    }

    [Fact]
    public async Task Settings_save_is_safe_when_nothing_changed()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        await store.SaveAsync(TestContext.Current.CancellationToken);
    }
}
