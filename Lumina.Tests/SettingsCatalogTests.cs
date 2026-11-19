using Lumina.Services.Configuration;

namespace Lumina.Tests;

/// <summary>P1-11 设置分组、prefs 清单与静态项标注。</summary>
public class SettingsCatalogTests
{
    [Fact]
    public void Settings_groups_match_documented_thirteen()
    {
        var expected = new[]
        {
            SettingsGroupCatalog.General,
            SettingsGroupCatalog.Appearance,
            SettingsGroupCatalog.Hotkeys,
            SettingsGroupCatalog.Clock,
            SettingsGroupCatalog.Schedule,
            SettingsGroupCatalog.Reminders,
            SettingsGroupCatalog.Clipboard,
            SettingsGroupCatalog.Capture,
            SettingsGroupCatalog.Countdown,
            SettingsGroupCatalog.Focus,
            SettingsGroupCatalog.Note,
            SettingsGroupCatalog.Vault,
            SettingsGroupCatalog.About,
        };

        Assert.Equal(13, SettingsGroupCatalog.All.Count);
        Assert.Equal(expected, SettingsGroupCatalog.All.Select(group => group.Id));
        Assert.All(SettingsGroupCatalog.All, group => Assert.NotEmpty(group.Title));
    }

    [Fact]
    public void Preference_catalog_contains_all_thirteen_effective_keys()
    {
        var required = new[]
        {
            "theme", "checkStyle", "skin", "clockFormat", "periodMode", "calLayout",
            "weekStart", "pomoFull", "audioVol", "schedRemind", "cdRemind",
            "inAppRemind", "sysNotify",
        };

        foreach (var key in required)
        {
            Assert.Contains(key, PreferenceKeys.All);
        }
    }
}
