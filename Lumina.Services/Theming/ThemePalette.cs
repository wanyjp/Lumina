namespace Lumina.Services.Theming;

/// <summary>
/// 单套主题颜色令牌。颜色使用 <c>#RRGGBB</c> 或 <c>#AARRGGBB</c> 文本，
/// 便于资源字典、测试与文档共用同一份数据；不依赖任何 UI 框架。
/// </summary>
public sealed record ThemePalette
{
    public required string Accent { get; init; }

    public required string AccentSecondary { get; init; }

    public required string AccentSoft { get; init; }

    public required string OnAccent { get; init; }

    public required string Wall { get; init; }

    public required string WallSecondary { get; init; }

    public required string MicaBase { get; init; }

    public required double MicaOpacity { get; init; }

    public required string CardBase { get; init; }

    public required double CardOpacity { get; init; }

    public required string CardSolid { get; init; }

    public required string Border { get; init; }

    public required string Line { get; init; }

    public required string Text { get; init; }

    public required string TextSecondary { get; init; }

    public required string TextTertiary { get; init; }

    public required string Hover { get; init; }

    public required string Active { get; init; }

    public required string Scrim { get; init; }

    public required string ScrimSoft { get; init; }

    public required string Edit { get; init; }

    public required string Danger { get; init; }

    public required string OnDanger { get; init; }

    /// <summary>倒计时五档紧迫度色：只表达紧迫度，不用于分类。</summary>
    public required string UrgencyCalm { get; init; }

    public required string UrgencyWarm { get; init; }

    public required string UrgencyHot { get; init; }

    public required string UrgencyUrgent { get; init; }

    public required string UrgencyExpired { get; init; }

    public required string ShadowColor { get; init; }

    public required double ShadowOpacity { get; init; }

    public required double ShadowBlurRadius { get; init; }

    public required double ShadowDepth { get; init; }

    public required string ShadowSoftColor { get; init; }

    public required double ShadowSoftOpacity { get; init; }

    public required double ShadowSoftBlurRadius { get; init; }

    public required double ShadowSoftDepth { get; init; }

    public required bool IsDark { get; init; }
}
