using Lumina.Domain.Enums;
using Lumina.Services.Configuration;

namespace Lumina.Tests;

/// <summary>P0-03 契约与配置基线测试；业务规则测试随对应阶段补充。</summary>
public class ConfigurationContractTests
{
    [Fact]
    public void Preference_catalog_has_33_keys_and_matching_defaults()
    {
        Assert.Equal(33, PreferenceKeys.All.Count);
        Assert.Equal(PreferenceKeys.All.Count, PreferenceDefaults.Values.Count);

        foreach (var key in PreferenceKeys.All)
        {
            Assert.True(PreferenceDefaults.Values.ContainsKey(key), $"缺少默认值：{key}");
        }
    }

    [Fact]
    public void Hotkey_catalog_has_7_global_and_2_module_bindings()
    {
        Assert.Equal(9, HotkeyCatalog.Defaults.Count);
        Assert.Equal(7, HotkeyCatalog.Defaults.Values.Count(x => x.Scope == HotkeyScope.Global));
        Assert.Equal(2, HotkeyCatalog.Defaults.Values.Count(x => x.Scope == HotkeyScope.Module));
    }

    [Fact]
    public void Hotkey_gesture_formats_modifiers_in_fixed_order()
    {
        var gesture = HotkeyCatalog.Defaults[HotkeyActions.ShowWindow];
        Assert.Equal("Ctrl + Alt + Space", gesture.ToString());
    }

    [Fact]
    public void Reminder_offsets_match_documented_lead_time_and_keys()
    {
        Assert.Equal(TimeSpan.Zero, ReminderOffset.None.ToLeadTime());
        Assert.Equal(TimeSpan.FromMinutes(15), ReminderOffset.Minutes15.ToLeadTime());
        Assert.Equal(TimeSpan.FromHours(1), ReminderOffset.Hour1.ToLeadTime());
        Assert.Equal(TimeSpan.FromDays(1), ReminderOffset.Day1.ToLeadTime());

        Assert.Equal("none", ReminderOffset.None.ToKey());
        Assert.Equal("m15", ReminderOffset.Minutes15.ToKey());
        Assert.Equal("d1", ReminderOffset.Day1.ToKey());
    }

    [Fact]
    public void App_paths_respect_data_root_override()
    {
        var original = Environment.GetEnvironmentVariable(AppPaths.DataRootOverrideVariable);
        var temp = Path.Combine(Path.GetTempPath(), "shiguang-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootOverrideVariable, temp);

            Assert.Equal(temp, AppPaths.Root);
            Assert.Equal(Path.Combine(temp, "db", "lumina.db"), AppPaths.DatabaseFile);
            Assert.Equal(Path.Combine(temp, "logs"), AppPaths.LogsDirectory);
            Assert.Equal(Path.Combine(temp, "logs", "app.log"), AppPaths.LogFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootOverrideVariable, original);
        }
    }
}
