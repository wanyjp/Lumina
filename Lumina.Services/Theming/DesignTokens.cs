namespace Lumina.Services.Theming;

/// <summary>
/// 跨主题稳定的设计令牌：圆角、描边、字号、字体与动效时长。
/// 颜色随主题变化，统一放在 <see cref="ThemePalette"/>；界面禁止硬编码本类之外的值。
/// </summary>
public static class DesignTokens
{
    public const double RadiusSmall = 6;
    public const double Radius = 8;
    public const double RadiusLarge = 12;
    public const double RadiusExtraLarge = 14;
    public const double RadiusPill = 999;

    public const double StrokeHairline = 1;
    public const double StrokeThin = 1.6;
    public const double StrokeThick = 2;

    public const double FontSizeCaption = 12;
    public const double FontSizeSmall = 12;
    public const double FontSizeBody = 13.5;
    public const double FontSizeSubtitle = 14;
    public const double FontSizeTitle = 20;
    public const double FontSizeDisplay = 44;
    public const double FontSizeClock = 58;
    public const double FontSizeTimer = 64;

    public const string FontFamilyBase = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei";
    public const string FontFamilyMono = "Cascadia Mono, Consolas, Lucida Console";

    public const int MotionFastMilliseconds = 150;
    public const int MotionNormalMilliseconds = 200;
    public const int MotionSlowMilliseconds = 260;
    public const int MotionDrawerMilliseconds = 320;

    public const double SpacingExtraSmall = 4;
    public const double SpacingSmall = 8;
    public const double SpacingMedium = 12;
    public const double SpacingLarge = 16;
    public const double SpacingExtraLarge = 24;
    public const double SpacingSection = 32;
}
