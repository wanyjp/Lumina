using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-11 快捷键绑定：默认值合并、持久化、冲突与空绑定。</summary>
public class HotkeyBindingTests
{
    [Fact]
    public void Action_catalog_covers_nine_documented_actions()
    {
        Assert.Equal(9, HotkeyActionCatalog.All.Count);
        Assert.Equal(HotkeyActions.All, HotkeyActionCatalog.All.Select(info => info.Action));
        Assert.All(HotkeyActionCatalog.All, info => Assert.NotEmpty(info.Title));
    }

    [Fact]
    public void Bindings_merge_defaults_and_persisted_overrides()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        Assert.Equal(9, service.Bindings().Count);
        Assert.Equal("Ctrl + Alt + N", service.Get(HotkeyActions.NewNote).ToString());

        var custom = new HotkeyGesture(
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            "K",
            HotkeyScope.Global);

        var result = service.Set(HotkeyActions.NewNote, custom);
        Assert.True(result.Saved);
        Assert.Equal("Ctrl + Shift + K", service.Get(HotkeyActions.NewNote).ToString());

        var reloaded = new HotkeyBindingService(CreateSettings(temp));
        Assert.Equal("Ctrl + Shift + K", reloaded.Get(HotkeyActions.NewNote).ToString());
        Assert.Equal("Ctrl + Alt + Space", reloaded.Get(HotkeyActions.ShowWindow).ToString());
    }

    [Fact]
    public void Full_table_conflict_rejects_save_and_keeps_previous()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        var result = service.Set(HotkeyActions.NewNote, service.Get(HotkeyActions.ShowWindow));

        Assert.False(result.Saved);
        Assert.Equal(HotkeyActions.ShowWindow, result.ConflictAction);
        Assert.Contains("唤起主窗口", result.Message);
        Assert.Equal("Ctrl + Alt + N", service.Get(HotkeyActions.NewNote).ToString());
    }

    [Fact]
    public void Empty_bindings_are_free_and_do_not_conflict()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        service.Clear(HotkeyActions.ShowWindow);
        Assert.True(HotkeyCatalog.IsUnbound(service.Get(HotkeyActions.ShowWindow)));

        var result = service.Set(
            HotkeyActions.NewNote,
            new HotkeyGesture(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                "Space",
                HotkeyScope.Global));

        Assert.True(result.Saved);
        Assert.Equal("Ctrl + Alt + Space", service.Get(HotkeyActions.NewNote).ToString());
    }

    [Fact]
    public void Missing_modifier_is_rejected()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        var result = service.Set(
            HotkeyActions.ShowWindow,
            new HotkeyGesture(HotkeyModifiers.None, "Space", HotkeyScope.Global));

        Assert.False(result.Saved);
        Assert.Contains("修饰键", result.Message);
        Assert.Equal("Ctrl + Alt + Space", service.Get(HotkeyActions.ShowWindow).ToString());
    }

    [Fact]
    public void Reset_single_and_all_restore_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        var custom = new HotkeyGesture(
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            "K",
            HotkeyScope.Global);

        service.Set(HotkeyActions.NewNote, custom);
        service.Reset(HotkeyActions.NewNote);
        Assert.Equal("Ctrl + Alt + N", service.Get(HotkeyActions.NewNote).ToString());

        service.Set(HotkeyActions.NewNote, custom);
        service.Clear(HotkeyActions.OpenClipboard);
        service.ResetAll();

        foreach (var (action, gesture) in HotkeyCatalog.Defaults)
        {
            Assert.Equal(gesture.ToString(), service.Get(action).ToString());
        }
    }

    [Fact]
    public void Unknown_action_is_rejected()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = new HotkeyBindingService(CreateSettings(temp));

        Assert.Throws<ArgumentException>(() => service.Get("unknown"));
        Assert.Throws<ArgumentException>(() => service.Set(
            "unknown",
            new HotkeyGesture(HotkeyModifiers.Control, "A", HotkeyScope.Global)));
    }

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));
}
