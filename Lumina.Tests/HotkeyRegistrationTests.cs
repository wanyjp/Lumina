using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-08 全局热键注册：全表冲突、空绑定、系统拒绝与幂等重注册。</summary>
public class HotkeyRegistrationTests
{
    [Fact]
    public void Register_all_covers_the_seven_wired_global_actions()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        var service = CreateService(temp, manager);

        var report = service.RegisterAll();

        Assert.Equal(7, report.Total);
        Assert.Equal(7, report.RegisteredCount);
        Assert.False(report.HasFailures);
        Assert.Equal(HotkeyRegistrationService.ActiveGlobalActions, report.Entries.Select(entry => entry.Action));
        Assert.Equal(7, manager.Registered.Count);

        // 模块内动作不进入系统注册。
        Assert.DoesNotContain(HotkeyActions.ColorPick, manager.Registered.Keys);
        Assert.DoesNotContain(HotkeyActions.CopyColorValue, manager.Registered.Keys);

        // P3-06 起「新建便签」已接入全局动作。
        Assert.Contains(HotkeyActions.NewNote, HotkeyRegistrationService.ActiveGlobalActions);
    }

    [Fact]
    public void Unbound_action_is_skipped_without_registering()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        var bindings = new HotkeyBindingService(CreateSettings(temp));
        var service = new HotkeyRegistrationService(manager, bindings);

        bindings.Clear(HotkeyActions.RegionCapture);

        var report = service.RegisterAll();
        var entry = report.Entries.Single(item => item.Action == HotkeyActions.RegionCapture);

        Assert.Equal(HotkeyRegistrationStatus.Skipped, entry.Status);
        Assert.False(manager.Registered.ContainsKey(HotkeyActions.RegionCapture));
        Assert.Equal(6, report.RegisteredCount);
    }

    [Fact]
    public void Duplicate_gesture_registers_first_action_and_reports_conflict()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        var manager = new FakeHotkeyManager();
        var bindings = new HotkeyBindingService(settings);
        var service = new HotkeyRegistrationService(manager, bindings);

        // 模拟被外部改坏的表：延时截图与区域截图使用同一组合。
        settings.Set(
            HotkeyBindingService.KeyFor(HotkeyActions.DelayedCapture),
            bindings.Get(HotkeyActions.RegionCapture),
            module: "hotkeys");

        var report = service.RegisterAll();
        var delayed = report.Entries.Single(item => item.Action == HotkeyActions.DelayedCapture);

        Assert.Equal(HotkeyRegistrationStatus.Conflict, delayed.Status);
        Assert.Equal(HotkeyActions.RegionCapture, delayed.ConflictAction);
        Assert.True(manager.Registered.ContainsKey(HotkeyActions.RegionCapture));
        Assert.False(manager.Registered.ContainsKey(HotkeyActions.DelayedCapture));
        Assert.Contains("重复", report.Summary);
    }

    [Fact]
    public void System_rejection_is_reported_and_other_actions_still_register()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        manager.Reject(HotkeyActions.FullScreenCapture);
        var service = CreateService(temp, manager);

        var report = service.RegisterAll();
        var full = report.Entries.Single(item => item.Action == HotkeyActions.FullScreenCapture);

        Assert.Equal(HotkeyRegistrationStatus.Rejected, full.Status);
        Assert.Equal(6, report.RegisteredCount);
        Assert.True(report.HasFailures);
        Assert.Contains("全屏截图", report.Summary);
        Assert.Contains("被其他程序占用", report.Summary);
    }

    [Fact]
    public void Register_all_is_idempotent_for_unlock_and_resume()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        var service = CreateService(temp, manager);

        var first = service.RegisterAll();
        var second = service.RegisterAll();

        Assert.Equal(7, first.RegisteredCount);
        Assert.Equal(7, second.RegisteredCount);
        Assert.Equal(7, manager.Registered.Count);
    }

    [Fact]
    public void Unregister_all_clears_every_registration()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        var service = CreateService(temp, manager);

        service.RegisterAll();
        service.UnregisterAll();

        Assert.Empty(manager.Registered);
    }

    [Fact]
    public void Try_register_detects_table_conflict_before_hitting_the_system()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var manager = new FakeHotkeyManager();
        var bindings = new HotkeyBindingService(CreateSettings(temp));
        var service = new HotkeyRegistrationService(manager, bindings);

        var duplicate = bindings.Get(HotkeyActions.RegionCapture);
        var ok = service.TryRegister(HotkeyActions.DelayedCapture, duplicate, out var conflict);

        Assert.False(ok);
        Assert.Equal(HotkeyActions.RegionCapture, conflict);
        Assert.Empty(manager.Registered);

        var free = new HotkeyGesture(
            HotkeyModifiers.Control | HotkeyModifiers.Alt,
            "F10",
            HotkeyScope.Global);

        Assert.True(service.TryRegister(HotkeyActions.DelayedCapture, free, out var none));
        Assert.Null(none);
        Assert.True(manager.Registered.ContainsKey(HotkeyActions.DelayedCapture));
    }

    private static HotkeyRegistrationService CreateService(TemporaryDataRoot temp, IHotkeyManager manager) =>
        new(manager, new HotkeyBindingService(CreateSettings(temp)));

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));

    private sealed class FakeHotkeyManager : IHotkeyManager
    {
        private readonly HashSet<string> _rejected = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HotkeyGesture> _registered = new(StringComparer.Ordinal);

        public event Action<string>? Pressed
        {
            add { }
            remove { }
        }

        public IReadOnlyDictionary<string, HotkeyGesture> Registered => _registered;

        public void Reject(string action) => _rejected.Add(action);

        public bool Register(string action, HotkeyGesture gesture)
        {
            if (_rejected.Contains(action))
            {
                return false;
            }

            _registered[action] = gesture;
            return true;
        }

        public bool IsFree(HotkeyGesture gesture, out string? conflictAction)
        {
            foreach (var (action, existing) in _registered)
            {
                if (HotkeyConflictDetector.Matches(existing, gesture))
                {
                    conflictAction = action;
                    return false;
                }
            }

            conflictAction = null;
            return true;
        }

        public void Unregister(string action) => _registered.Remove(action);

        public void UnregisterAll() => _registered.Clear();
    }
}
