using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Services.Completion;
using Lumina.Services.Configuration;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-10 完成风格：四套策略、状态映射、持久化与减少动画。</summary>
public class CompletionStyleTests
{
    [Fact]
    public void Catalog_has_four_strategies_and_defaults_to_stroke()
    {
        Assert.Equal(4, CompletionStyleCatalog.All.Count);
        Assert.Equal(
            new[] { CompletionStyle.Stroke, CompletionStyle.Fill, CompletionStyle.Ring, CompletionStyle.Dot },
            CompletionStyleCatalog.All.Select(strategy => strategy.Style));
        Assert.Equal(CompletionStyle.Stroke, CompletionStyleCatalog.Parse(null));
        Assert.Equal(CompletionStyle.Stroke, CompletionStyleCatalog.Parse("unknown"));
        Assert.Equal("fill", CompletionStyleCatalog.Key(CompletionStyle.Fill));
        Assert.Equal(CompletionStyle.Ring, CompletionStyleCatalog.Parse("ring"));
    }

    [Fact]
    public void Strategies_propagate_done_and_reduced_motion()
    {
        foreach (var strategy in CompletionStyleCatalog.All)
        {
            var done = strategy.Render(done: true, reducedMotion: false);
            Assert.Equal(strategy.Style, done.Style);
            Assert.True(done.Done);
            Assert.False(done.ReducedMotion);

            var reduced = strategy.Render(done: false, reducedMotion: true);
            Assert.Equal(strategy.Style, reduced.Style);
            Assert.False(reduced.Done);
            Assert.True(reduced.ReducedMotion);
        }
    }

    [Fact]
    public void Hub_persists_style_and_reduced_motion()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        var hub = new CompletionStyleHub(settings);

        Assert.Equal(CompletionStyle.Stroke, hub.Current);
        Assert.False(hub.ReducedMotion);

        var styleChanges = 0;
        var motionChanges = 0;
        hub.Changed += _ => styleChanges++;
        hub.MotionChanged += () => motionChanges++;

        hub.Set(CompletionStyle.Fill);
        hub.SetReducedMotion(true);

        Assert.Equal(CompletionStyle.Fill, hub.Current);
        Assert.True(hub.ReducedMotion);
        Assert.Equal(1, styleChanges);
        Assert.Equal(1, motionChanges);
        Assert.Equal("fill", settings.Get<string>(PreferenceKeys.CheckStyle));
        Assert.True(settings.Get<bool>(PreferenceKeys.ReduceMotion));

        var reloaded = new CompletionStyleHub(CreateSettings(temp));
        Assert.Equal(CompletionStyle.Fill, reloaded.Current);
        Assert.True(reloaded.ReducedMotion);
    }

    [Fact]
    public void Hub_ignores_same_value()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var hub = new CompletionStyleHub(CreateSettings(temp));
        var changes = 0;
        var motionChanges = 0;
        hub.Changed += _ => changes++;
        hub.MotionChanged += () => motionChanges++;

        hub.Set(CompletionStyle.Stroke);
        hub.SetReducedMotion(false);

        Assert.Equal(0, changes);
        Assert.Equal(0, motionChanges);
    }

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));
}
