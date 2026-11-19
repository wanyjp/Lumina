using Lumina.Data.Settings;
using Lumina.Services.Clipboard;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-02 忽略规则：默认值、大小写不敏感、去重与损坏值回落。</summary>
public class IgnoreRuleServiceTests
{
    [Fact]
    public void Default_rules_cover_documented_password_managers()
    {
        Assert.Equal(
            new[] { "1Password.exe", "KeePassXC.exe", "Bitwarden.exe" },
            IgnoreRuleService.DefaultRules.ToArray());
    }

    [Fact]
    public void Rules_match_process_and_title_case_insensitively()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        Assert.True(service.ShouldIgnoreSource("密码库主窗口", "1Password.exe"));
        Assert.True(service.ShouldIgnoreSource("任意标题", "KEEPASSXC.EXE"));
        Assert.False(service.ShouldIgnoreSource("Bitwarden — 登录", "chrome.exe"));
        Assert.False(service.ShouldIgnoreSource("记事本", "notepad.exe"));
        Assert.False(service.ShouldIgnoreSource(null, null));

        service.AddRule("Bitwarden");
        Assert.True(service.ShouldIgnoreSource("Bitwarden — 登录", "chrome.exe"));
    }

    [Fact]
    public void Add_and_remove_rules_deduplicate_case_insensitively()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        Assert.True(service.AddRule("  MyVault.exe  "));
        Assert.False(service.AddRule("myvault.EXE"));
        Assert.False(service.AddRule("   "));
        Assert.Contains("MyVault.exe", service.Rules);

        Assert.True(service.RemoveRule("MYVAULT.exe"));
        Assert.False(service.RemoveRule("MyVault.exe"));
        Assert.DoesNotContain("MyVault.exe", service.Rules);
    }

    [Fact]
    public void Saving_an_empty_list_does_not_fall_back_to_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var saved = service.SaveRules(Array.Empty<string>());

        Assert.Empty(saved);
        Assert.Empty(service.Rules);
        Assert.False(service.ShouldIgnoreSource("1Password", "1Password.exe"));
    }

    [Fact]
    public void Reset_defaults_restores_documented_rules()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        service.SaveRules(["custom.exe"]);
        var reset = service.ResetDefaults();

        Assert.Equal(IgnoreRuleService.DefaultRules.ToArray(), reset.ToArray());
        Assert.Equal(IgnoreRuleService.DefaultRules.ToArray(), service.Rules.ToArray());
    }

    [Fact]
    public void Corrupted_preference_falls_back_to_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        store.Set(PreferenceKeys.ClipboardIgnoreRules, "not-json");
        var service = new IgnoreRuleService(store);

        Assert.Equal(IgnoreRuleService.DefaultRules.ToArray(), service.Rules.ToArray());
    }

    [Fact]
    public void One_time_code_switch_defaults_on_and_persists_off()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = new SettingsStore(new SqliteSettingsBackend(temp.Database));
        var service = new IgnoreRuleService(store);

        Assert.True(service.BlockOneTimeCodes);

        service.SetBlockOneTimeCodes(false);

        Assert.False(service.BlockOneTimeCodes);
        var reloaded = new IgnoreRuleService(
            new SettingsStore(new SqliteSettingsBackend(temp.Database)));
        Assert.False(reloaded.BlockOneTimeCodes);
    }

    private static IgnoreRuleService CreateService(TemporaryDataRoot temp) =>
        new(new SettingsStore(new SqliteSettingsBackend(temp.Database)));
}
