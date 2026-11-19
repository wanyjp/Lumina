using Lumina.Services.Configuration;
using Lumina.Services.Theming;

namespace Lumina.Tests;

/// <summary>P0-05 主题目录、自动跟随与对比度基线。</summary>
public class ThemeCatalogTests
{
    [Fact]
    public void Catalog_has_five_themes_and_expected_default()
    {
        Assert.Equal(5, ThemeCatalog.All.Count);
        Assert.Equal(
            new[] { ThemeCatalog.Auto, ThemeCatalog.Light, ThemeCatalog.Dark, ThemeCatalog.White, ThemeCatalog.Neon },
            ThemeCatalog.All);
        Assert.Equal(PreferenceDefaults.Theme, ThemeCatalog.Auto);
        Assert.All(ThemeCatalog.All, id => Assert.True(ThemeCatalog.DisplayNames.ContainsKey(id)));
    }

    [Fact]
    public void Normalize_falls_back_to_auto_for_unknown_values()
    {
        Assert.Equal(ThemeCatalog.Auto, ThemeCatalog.Normalize(null));
        Assert.Equal(ThemeCatalog.Auto, ThemeCatalog.Normalize(""));
        Assert.Equal(ThemeCatalog.Auto, ThemeCatalog.Normalize("unknown"));
        Assert.Equal(ThemeCatalog.Dark, ThemeCatalog.Normalize(" DARK "));
        Assert.Equal(ThemeCatalog.Neon, ThemeCatalog.Normalize("neon"));
    }

    [Fact]
    public void Auto_theme_follows_system_and_fixed_theme_ignores_it()
    {
        var lightAuto = ThemeCatalog.Resolve(ThemeCatalog.Auto, systemIsDark: false);
        var darkAuto = ThemeCatalog.Resolve(ThemeCatalog.Auto, systemIsDark: true);

        Assert.Equal(ThemeCatalog.Auto, lightAuto.RequestedTheme);
        Assert.Equal(ThemeCatalog.Light, lightAuto.EffectiveTheme);
        Assert.False(lightAuto.IsDark);

        Assert.Equal(ThemeCatalog.Auto, darkAuto.RequestedTheme);
        Assert.Equal(ThemeCatalog.Dark, darkAuto.EffectiveTheme);
        Assert.True(darkAuto.IsDark);

        var fixedDark = ThemeCatalog.Resolve(ThemeCatalog.Dark, systemIsDark: false);
        Assert.Equal(ThemeCatalog.Dark, fixedDark.EffectiveTheme);
        Assert.True(fixedDark.IsDark);

        var fixedWhite = ThemeCatalog.Resolve(ThemeCatalog.White, systemIsDark: true);
        Assert.Equal(ThemeCatalog.White, fixedWhite.EffectiveTheme);
        Assert.False(fixedWhite.IsDark);
    }

    [Fact]
    public void Theme_palettes_meet_contrast_baseline()
    {
        foreach (var (themeId, palette) in ThemeCatalog.Palettes)
        {
            var body = ColorContrast.Ratio(palette.Text, palette.CardSolid);
            var secondary = ColorContrast.Ratio(palette.TextSecondary, palette.CardSolid);
            var button = ColorContrast.Ratio(palette.OnAccent, palette.Accent);

            Assert.True(body >= 7.0, $"{themeId} 正文对比度不足：{body:F2}");
            Assert.True(secondary >= 4.5, $"{themeId} 次要文字对比度不足：{secondary:F2}");
            Assert.True(button >= 4.5, $"{themeId} 强调按钮对比度不足：{button:F2}");
        }
    }

    [Fact]
    public void Urgency_colors_are_distinct_and_readable_on_cards()
    {
        foreach (var (themeId, palette) in ThemeCatalog.Palettes)
        {
            var urgency = new[]
            {
                palette.UrgencyCalm,
                palette.UrgencyWarm,
                palette.UrgencyHot,
                palette.UrgencyUrgent,
                palette.UrgencyExpired,
            };

            Assert.Equal(5, urgency.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            foreach (var color in urgency)
            {
                var ratio = ColorContrast.Ratio(color, palette.CardSolid);
                Assert.True(ratio >= 3.0, $"{themeId} 紧迫度色对比度不足：{color} → {ratio:F2}");
            }
        }
    }

    [Fact]
    public void Design_tokens_are_positive_and_use_expected_font_stacks()
    {
        Assert.True(DesignTokens.RadiusSmall > 0);
        Assert.True(DesignTokens.Radius < DesignTokens.RadiusLarge);
        Assert.True(DesignTokens.StrokeHairline < DesignTokens.StrokeThick);
        Assert.True(DesignTokens.FontSizeCaption < DesignTokens.FontSizeTitle);
        Assert.Contains("Segoe UI", DesignTokens.FontFamilyBase, StringComparison.Ordinal);
        Assert.Contains("Microsoft YaHei", DesignTokens.FontFamilyBase, StringComparison.Ordinal);
        Assert.Contains("Cascadia Mono", DesignTokens.FontFamilyMono, StringComparison.Ordinal);
        Assert.InRange(DesignTokens.MotionFastMilliseconds, 100, 300);
        Assert.InRange(DesignTokens.MotionDrawerMilliseconds, 200, 400);
    }
}
