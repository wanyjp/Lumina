using Lumina.Data.Settings;
using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P0-07 共享时钟与小组件布局格式验证。</summary>
public class WidgetAndClockTests
{
    [Fact]
    public void Clock_service_returns_consistent_snapshot()
    {
        using var clock = new ClockService();
        var snapshot = clock.Now(use12Hour: false, shichenMode: false);

        Assert.Equal(snapshot.Local.ToUniversalTime(), snapshot.Utc);
        Assert.Equal(DateOnly.FromDateTime(snapshot.Local.DateTime), snapshot.Date);
        Assert.Empty(clock.WorldClocks());
    }

    [Fact]
    public void Widget_state_roundtrips_through_settings_store()
    {
        using var temp = new TemporaryDataRoot();
        var file = Path.Combine(temp.ConfigDirectory, "widget-layout.json");
        var store = new SettingsStore(new JsonSettingsBackend(file));

        var state = new WidgetState(
            WidgetKind.Clock,
            X: 120.5,
            Y: 340.0,
            Width: 300,
            Height: 180,
            Opacity: 0.85,
            Topmost: false,
            ClickThrough: true)
        {
            SourceId = 42,
            Skin = WidgetSkin.LiquidGlass,
        };

        store.Set("widget.clock.state.v1", state, "widget");
        var reloaded = store.Get<WidgetState>("widget.clock.state.v1");

        Assert.NotNull(reloaded);
        Assert.Equal(WidgetKind.Clock, reloaded.Kind);
        Assert.Equal(120.5, reloaded.X);
        Assert.Equal(340.0, reloaded.Y);
        Assert.Equal(300, reloaded.Width);
        Assert.Equal(180, reloaded.Height);
        Assert.Equal(0.85, reloaded.Opacity);
        Assert.False(reloaded.Topmost);
        Assert.True(reloaded.ClickThrough);
        Assert.Equal(42, reloaded.SourceId);
        Assert.Equal(WidgetSkin.LiquidGlass, reloaded.Skin);
    }

    [Fact]
    public void Widget_state_defaults_to_acrylic_skin()
    {
        var state = new WidgetState(
            WidgetKind.Countdown,
            X: 0,
            Y: 0,
            Width: 260,
            Height: 220);

        Assert.Equal(WidgetSkin.Acrylic, state.Skin);
    }

    [Fact]
    public void Widget_skin_catalog_has_four_unique_stable_options()
    {
        Assert.Equal(4, WidgetSkinCatalog.All.Count);
        Assert.Equal(
            new[] { WidgetSkin.Opaque, WidgetSkin.Transparent, WidgetSkin.Acrylic, WidgetSkin.LiquidGlass },
            WidgetSkinCatalog.All.Select(option => option.Value));
        Assert.Equal(
            4,
            WidgetSkinCatalog.All.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            4,
            WidgetSkinCatalog.All.Select(option => option.Label).Distinct(StringComparer.Ordinal).Count());

        foreach (var option in WidgetSkinCatalog.All)
        {
            Assert.Equal(option.Value, WidgetSkinCatalog.Parse(option.Key));
            Assert.Equal(option.Key, WidgetSkinCatalog.KeyOf(option.Value));
            Assert.False(string.IsNullOrWhiteSpace(option.Description));
        }

        Assert.Equal(WidgetSkin.Acrylic, WidgetSkinCatalog.Parse("unknown"));
        Assert.Equal(WidgetSkin.Acrylic, WidgetSkinCatalog.Parse(null));
        Assert.Equal(WidgetSkin.Transparent, WidgetSkinCatalog.Parse(" TRANSPARENT "));
        Assert.Equal(WidgetSkin.Acrylic, WidgetSkinCatalog.Normalize((WidgetSkin)99));
    }
}
