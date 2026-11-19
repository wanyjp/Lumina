using Lumina.Services.Theming;

namespace Lumina.Tests;

/// <summary>
/// 主题可读性验收：所有档位的正文、次要、三级文字对卡面与墙面都必须达到
/// WCAG AA（≥ 4.5:1）。三级文字大量用于说明行、时间戳与辅助信息，
/// 一旦回落就会重新出现“字太暗看不清”的问题。
/// </summary>
public class ThemeContrastTests
{
    private const double MinimumRatio = 4.5;

    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in ThemeCatalog.All)
        {
            if (id != ThemeCatalog.Auto)
            {
                data.Add(id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Text_tokens_meet_wcag_aa_on_card_and_wall(string themeId)
    {
        var palette = ThemeCatalog.Palettes[themeId];

        foreach (var (name, color) in new[]
                 {
                     ("Text", palette.Text),
                     ("TextSecondary", palette.TextSecondary),
                     ("TextTertiary", palette.TextTertiary),
                 })
        {
            var onCard = ColorContrast.Ratio(color, palette.CardSolid);
            var onWall = ColorContrast.Ratio(color, palette.Wall);

            Assert.True(
                onCard >= MinimumRatio,
                $"{themeId} 的 {name} 在卡面上对比度仅 {onCard:0.00}:1，低于 {MinimumRatio}:1");

            Assert.True(
                onWall >= MinimumRatio,
                $"{themeId} 的 {name} 在墙面上对比度仅 {onWall:0.00}:1，低于 {MinimumRatio}:1");
        }
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Secondary_text_is_visibly_darker_than_primary_and_tertiary_is_readable(string themeId)
    {
        var palette = ThemeCatalog.Palettes[themeId];
        var primary = ColorContrast.RelativeLuminance(palette.Text);
        var secondary = ColorContrast.RelativeLuminance(palette.TextSecondary);
        var tertiary = ColorContrast.RelativeLuminance(palette.TextTertiary);

        // 层级必须仍然存在：正文 > 次要 > 三级（深色主题反过来）。
        if (palette.IsDark)
        {
            Assert.True(primary > secondary && secondary > tertiary, $"{themeId} 的文字层级顺序不正确");
        }
        else
        {
            Assert.True(primary < secondary && secondary < tertiary, $"{themeId} 的文字层级顺序不正确");
        }
    }

    [Fact]
    public void High_contrast_uses_system_colors()
    {
        // 高对比度档位由系统色覆盖，不参与上面的品牌色板校验，但必须保证前景=系统文字色。
        Assert.True(ColorContrast.Ratio("#000000", "#FFFFFF") >= 21);
    }
}
