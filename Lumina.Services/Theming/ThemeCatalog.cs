namespace Lumina.Services.Theming;

/// <summary>主题解析结果：保留用户选择，并给出最终生效档位。</summary>
public sealed record ThemeResolution(
    string RequestedTheme,
    string EffectiveTheme,
    bool IsDark,
    ThemePalette Palette);

/// <summary>
/// 五档主题目录：跟随系统 / 浅色 / 深色 / 极简白 / 深色霓虹。
/// 颜色值为唯一事实源，UI 资源字典由数据层生成，禁止在 XAML 中硬编码色值。
/// 正文 / 次要 / 三级文字与卡面、墙面的对比度均需 ≥ 4.5:1（WCAG AA），
/// 由 <c>ThemeContrastTests</c> 守护；调整色值时先跑测试。
/// </summary>
public static class ThemeCatalog
{
    public const string Auto = "auto";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string White = "white";
    public const string Neon = "neon";

    public static IReadOnlyList<string> All { get; } = [Auto, Light, Dark, White, Neon];

    public static IReadOnlyDictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>
    {
        [Auto] = "跟随系统",
        [Light] = "浅色",
        [Dark] = "深色",
        [White] = "极简白",
        [Neon] = "深色霓虹",
    };

    public static IReadOnlyDictionary<string, ThemePalette> Palettes { get; } = new Dictionary<string, ThemePalette>
    {
        [Light] = new ThemePalette
        {
            Accent = "#0078D4",
            AccentSecondary = "#0078D4",
            AccentSoft = "#1F0078D4",
            OnAccent = "#FFFFFF",
            Wall = "#DFE6F0",
            WallSecondary = "#C9D6E8",
            MicaBase = "#F8F9FB",
            MicaOpacity = 0.78,
            CardBase = "#FFFFFF",
            CardOpacity = 0.72,
            CardSolid = "#FFFFFF",
            Border = "#14000000",
            Line = "#0F000000",
            Text = "#1A1C1F",
            TextSecondary = "#454B54",
            TextTertiary = "#5F6672",
            Hover = "#0D000000",
            Active = "#17000000",
            Scrim = "#73080C16",
            ScrimSoft = "#47000000",
            Edit = "#3A8EE6",
            Danger = "#E0362B",
            OnDanger = "#FFFFFF",
            UrgencyCalm = "#2F6FED",
            UrgencyWarm = "#B26A12",
            UrgencyHot = "#C0561F",
            UrgencyUrgent = "#C62828",
            UrgencyExpired = "#6B7280",
            ShadowColor = "#1C2C54",
            ShadowOpacity = 0.18,
            ShadowBlurRadius = 40,
            ShadowDepth = 12,
            ShadowSoftColor = "#1C2C54",
            ShadowSoftOpacity = 0.10,
            ShadowSoftBlurRadius = 14,
            ShadowSoftDepth = 4,
            IsDark = false,
        },
        [Dark] = new ThemePalette
        {
            Accent = "#4CC2FF",
            AccentSecondary = "#4CC2FF",
            AccentSoft = "#244CC2FF",
            OnAccent = "#082338",
            Wall = "#17181C",
            WallSecondary = "#23262D",
            MicaBase = "#282A2F",
            MicaOpacity = 0.78,
            CardBase = "#36393F",
            CardOpacity = 0.70,
            CardSolid = "#32353B",
            Border = "#17FFFFFF",
            Line = "#12FFFFFF",
            Text = "#F4F5F7",
            TextSecondary = "#D3D8E0",
            TextTertiary = "#B6BDC8",
            Hover = "#12FFFFFF",
            Active = "#1CFFFFFF",
            Scrim = "#8C080C16",
            ScrimSoft = "#4F000000",
            Edit = "#3A8EE6",
            Danger = "#E0362B",
            OnDanger = "#FFFFFF",
            UrgencyCalm = "#6FA8FF",
            UrgencyWarm = "#E0A64A",
            UrgencyHot = "#FF8A5C",
            UrgencyUrgent = "#FF6B5E",
            UrgencyExpired = "#B6BDC8",
            ShadowColor = "#000000",
            ShadowOpacity = 0.55,
            ShadowBlurRadius = 40,
            ShadowDepth = 12,
            ShadowSoftColor = "#000000",
            ShadowSoftOpacity = 0.25,
            ShadowSoftBlurRadius = 14,
            ShadowSoftDepth = 4,
            IsDark = true,
        },
        [White] = new ThemePalette
        {
            Accent = "#2F6FED",
            AccentSecondary = "#2F6FED",
            AccentSoft = "#1A2F6FED",
            OnAccent = "#FFFFFF",
            Wall = "#FFFFFF",
            WallSecondary = "#F6F8FC",
            MicaBase = "#FFFFFF",
            MicaOpacity = 1.0,
            CardBase = "#FFFFFF",
            CardOpacity = 1.0,
            CardSolid = "#FFFFFF",
            Border = "#DCDFE6",
            Line = "#E9EBF0",
            Text = "#15181D",
            TextSecondary = "#464C56",
            TextTertiary = "#5C636E",
            Hover = "#F5F7FC",
            Active = "#ECF1FB",
            Scrim = "#73080C16",
            ScrimSoft = "#47000000",
            Edit = "#3A8EE6",
            Danger = "#E0362B",
            OnDanger = "#FFFFFF",
            UrgencyCalm = "#2F6FED",
            UrgencyWarm = "#B26A12",
            UrgencyHot = "#C0561F",
            UrgencyUrgent = "#C62828",
            UrgencyExpired = "#6B7280",
            ShadowColor = "#182A5A",
            ShadowOpacity = 0.08,
            ShadowBlurRadius = 26,
            ShadowDepth = 8,
            ShadowSoftColor = "#182A5A",
            ShadowSoftOpacity = 0.05,
            ShadowSoftBlurRadius = 10,
            ShadowSoftDepth = 3,
            IsDark = false,
        },
        [Neon] = new ThemePalette
        {
            Accent = "#00E5FF",
            AccentSecondary = "#A855F7",
            AccentSoft = "#1A00E5FF",
            OnAccent = "#05141C",
            Wall = "#080812",
            WallSecondary = "#0E0E1F",
            MicaBase = "#141426",
            MicaOpacity = 0.72,
            CardBase = "#1A1A30",
            CardOpacity = 0.66,
            CardSolid = "#14142B",
            Border = "#3300E5FF",
            Line = "#14FFFFFF",
            Text = "#E9EDFF",
            TextSecondary = "#B4BCE2",
            TextTertiary = "#A6AED6",
            Hover = "#1400E5FF",
            Active = "#2400E5FF",
            Scrim = "#8C000000",
            ScrimSoft = "#4F000000",
            Edit = "#3A8EE6",
            Danger = "#E0362B",
            OnDanger = "#FFFFFF",
            UrgencyCalm = "#00E5FF",
            UrgencyWarm = "#FFB35C",
            UrgencyHot = "#FF8A5C",
            UrgencyUrgent = "#FF5C7A",
            UrgencyExpired = "#A6AED6",
            ShadowColor = "#00E5FF",
            ShadowOpacity = 0.14,
            ShadowBlurRadius = 34,
            ShadowDepth = 0,
            ShadowSoftColor = "#000000",
            ShadowSoftOpacity = 0.60,
            ShadowSoftBlurRadius = 40,
            ShadowSoftDepth = 12,
            IsDark = true,
        },
    };

    /// <summary>把任意输入规范为已知档位；空值或未知值回落到跟随系统。</summary>
    public static string Normalize(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return Auto;
        }

        var normalized = themeId.Trim().ToLowerInvariant();
        return All.Contains(normalized) ? normalized : Auto;
    }

    /// <summary>解析主题：auto 由系统明暗决定，固定档位忽略系统状态。</summary>
    public static ThemeResolution Resolve(string? themeId, bool systemIsDark)
    {
        var requested = Normalize(themeId);
        var effective = requested == Auto
            ? (systemIsDark ? Dark : Light)
            : requested;

        return new ThemeResolution(requested, effective, effective is Dark or Neon, Palettes[effective]);
    }

    public static string DisplayName(string? themeId)
    {
        var normalized = Normalize(themeId);
        return DisplayNames.TryGetValue(normalized, out var name) ? name : DisplayNames[Auto];
    }
}
