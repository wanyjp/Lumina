using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Tests;

/// <summary>快捷键全表冲突检测。</summary>
public class HotkeyConflictTests
{
    [Fact]
    public void Default_hotkeys_are_unique()
    {
        var bindings = HotkeyCatalog.Defaults;
        var actions = bindings.Keys.ToList();

        foreach (var action in actions)
        {
            var others = bindings
                .Where(pair => pair.Key != action)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            Assert.Null(HotkeyConflictDetector.FindConflict(others, action, bindings[action]));
        }
    }

    [Fact]
    public void Conflict_detector_returns_occupying_action()
    {
        var bindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal)
        {
            ["show"] = HotkeyCatalog.Defaults[HotkeyActions.ShowWindow],
            ["newnote"] = HotkeyCatalog.Defaults[HotkeyActions.NewNote],
        };

        var conflict = HotkeyConflictDetector.FindConflict(
            bindings,
            "clipboard",
            HotkeyCatalog.Defaults[HotkeyActions.ShowWindow]);

        Assert.Equal("show", conflict);
    }
}
