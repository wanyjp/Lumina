using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Lumina.Services.Theming;

namespace Lumina.UI.Wpf.Themes;

/// <summary>
/// 把 <see cref="ThemeResolution"/> 编译为 WPF 资源字典。
/// 所有颜色、圆角、描边、阴影与动效时长都来自唯一令牌源，XAML 只引用资源键。
/// </summary>
public static class ThemeResourceBuilder
{
    public static ResourceDictionary Build(
        ThemeResolution resolution,
        bool highContrast,
        bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var palette = highContrast ? BuildHighContrastPalette(resolution.Palette) : resolution.Palette;
        var dictionary = new ResourceDictionary();

        dictionary["Theme.AccentBrush"] = CreateBrush(palette.Accent);
        dictionary["Theme.AccentSecondaryBrush"] = CreateBrush(palette.AccentSecondary);
        dictionary["Theme.AccentSoftBrush"] = CreateBrush(palette.AccentSoft);
        dictionary["Theme.OnAccentBrush"] = CreateBrush(palette.OnAccent);
        dictionary["Theme.WallBrush"] = BuildWallBrush(palette);
        dictionary["Theme.WallSecondaryBrush"] = CreateBrush(palette.WallSecondary);
        dictionary["Theme.GlowBrush"] = BuildGlowBrush(palette, highContrast);
        dictionary["Theme.MicaBrush"] = BuildTranslucentBrush(palette.MicaBase, palette.MicaOpacity);
        dictionary["Theme.CardBrush"] = BuildTranslucentBrush(palette.CardBase, palette.CardOpacity);
        dictionary["Theme.CardSolidBrush"] = CreateBrush(palette.CardSolid);

        // 小组件四套皮肤：表面、描边、厚度与阴影都由主题令牌派生，切换主题即时生效。
        AddWidgetSkinResources(dictionary, palette, highContrast);
        dictionary["Theme.BorderBrush"] = CreateBrush(palette.Border);
        dictionary["Theme.LineBrush"] = CreateBrush(palette.Line);
        dictionary["Theme.TextBrush"] = CreateBrush(palette.Text);
        dictionary["Theme.TextSecondaryBrush"] = CreateBrush(palette.TextSecondary);
        dictionary["Theme.TextTertiaryBrush"] = CreateBrush(palette.TextTertiary);
        dictionary["Theme.HoverBrush"] = CreateBrush(palette.Hover);
        dictionary["Theme.ActiveBrush"] = CreateBrush(palette.Active);
        dictionary["Theme.ScrimBrush"] = CreateBrush(palette.Scrim);
        dictionary["Theme.ScrimSoftBrush"] = CreateBrush(palette.ScrimSoft);
        dictionary["Theme.EditBrush"] = CreateBrush(palette.Edit);
        dictionary["Theme.DangerBrush"] = CreateBrush(palette.Danger);
        dictionary["Theme.OnDangerBrush"] = CreateBrush(palette.OnDanger);

        // 倒计时五档紧迫度：颜色只表达紧迫度，分类标签保持中性描边。
        dictionary["Theme.Urgency.CalmBrush"] = CreateBrush(palette.UrgencyCalm);
        dictionary["Theme.Urgency.WarmBrush"] = CreateBrush(palette.UrgencyWarm);
        dictionary["Theme.Urgency.HotBrush"] = CreateBrush(palette.UrgencyHot);
        dictionary["Theme.Urgency.UrgentBrush"] = CreateBrush(palette.UrgencyUrgent);
        dictionary["Theme.Urgency.ExpiredBrush"] = CreateBrush(palette.UrgencyExpired);

        // 小组件文字：比页面正文更“实”，避免深色卡面上出现灰字；浅色主题自动变深色。
        dictionary["Theme.Widget.TextBrush"] = CreateBrush(palette.Text);
        dictionary["Theme.Widget.TextSecondaryBrush"] = CreateBrush(WithAlpha(palette.Text, 0xF0));
        dictionary["Theme.Widget.TextTertiaryBrush"] = CreateBrush(WithAlpha(palette.Text, 0xD8));
        // 仅给文字加一层极轻的投影：深色玻璃用暗影、浅色玻璃用亮影，
        // 保证文字在透出的桌面内容上仍能辨认；不含卡片外围阴影。
        var cardColor = ParseColor(palette.CardSolid);
        var darkGlass = ((0.299 * cardColor.R) + (0.587 * cardColor.G) + (0.114 * cardColor.B)) < 110;
        dictionary["Theme.WidgetTextShadow"] = highContrast
            ? BuildShadow("#000000", 0, 0, 0)
            : darkGlass
                ? BuildShadow("#000000", 0.45, 3, 0)
                : BuildShadow("#FFFFFF", 0.55, 3, 0);

        // 输入框与滚动条需要比卡片描边更明显的层次：都由文字色派生，保证明暗主题一致可读。
        dictionary["Theme.FieldBrush"] = CreateBrush(Blend(palette.CardSolid, palette.Text, 0.07));
        // 输入框描边需要有足够的亮度差：深色主题下 0x5A 太淡，地点/备注等空框几乎看不见。
        dictionary["Theme.FieldLineBrush"] = CreateBrush(WithAlpha(palette.Text, 0x78));
        dictionary["Theme.ScrollThumbBrush"] = CreateBrush(WithAlpha(palette.Text, 0x3D));
        dictionary["Theme.ScrollThumbHoverBrush"] = CreateBrush(WithAlpha(palette.Text, 0x78));

        // 颜色令牌：系统控件（如 Calendar）需要 Color 而非 Brush，只能以颜色形式暴露。
        dictionary["Theme.Color.CardSolid"] = ParseColor(palette.CardSolid);
        dictionary["Theme.Color.Text"] = ParseColor(palette.Text);
        dictionary["Theme.Color.TextTertiary"] = ParseColor(palette.TextTertiary);
        dictionary["Theme.Color.Accent"] = ParseColor(palette.Accent);
        dictionary["Theme.Color.OnAccent"] = ParseColor(palette.OnAccent);
        dictionary["Theme.Color.Urgency.Calm"] = ParseColor(palette.UrgencyCalm);
        dictionary["Theme.Color.Urgency.Warm"] = ParseColor(palette.UrgencyWarm);
        dictionary["Theme.Color.Urgency.Hot"] = ParseColor(palette.UrgencyHot);
        dictionary["Theme.Color.Urgency.Urgent"] = ParseColor(palette.UrgencyUrgent);
        dictionary["Theme.Color.Urgency.Expired"] = ParseColor(palette.UrgencyExpired);

        dictionary["Theme.Shadow"] = BuildShadow(palette.ShadowColor, palette.ShadowOpacity, palette.ShadowBlurRadius, palette.ShadowDepth);
        dictionary["Theme.ShadowSoft"] = BuildShadow(
            palette.ShadowSoftColor,
            palette.ShadowSoftOpacity,
            palette.ShadowSoftBlurRadius,
            palette.ShadowSoftDepth);

        dictionary["Theme.Radius.Small"] = new CornerRadius(highContrast ? 0 : DesignTokens.RadiusSmall);
        dictionary["Theme.Radius.Default"] = new CornerRadius(highContrast ? 0 : DesignTokens.Radius);
        dictionary["Theme.Radius.Large"] = new CornerRadius(highContrast ? 0 : DesignTokens.RadiusLarge);
        dictionary["Theme.Radius.ExtraLarge"] = new CornerRadius(highContrast ? 0 : DesignTokens.RadiusExtraLarge);
        dictionary["Theme.Radius.Pill"] = new CornerRadius(highContrast ? 0 : DesignTokens.RadiusPill);

        var hairline = highContrast ? DesignTokens.StrokeThick : DesignTokens.StrokeHairline;
        var thin = highContrast ? DesignTokens.StrokeThick : DesignTokens.StrokeThin;
        dictionary["Theme.Stroke.Hairline"] = new Thickness(hairline);
        dictionary["Theme.Stroke.Thin"] = new Thickness(thin);
        dictionary["Theme.Stroke.Thick"] = new Thickness(DesignTokens.StrokeThick);

        dictionary["Theme.FontFamily"] = new FontFamily(DesignTokens.FontFamilyBase);
        dictionary["Theme.FontFamilyMono"] = new FontFamily(DesignTokens.FontFamilyMono);
        dictionary["Theme.FontSize.Caption"] = DesignTokens.FontSizeCaption;
        dictionary["Theme.FontSize.Small"] = DesignTokens.FontSizeSmall;
        dictionary["Theme.FontSize.Body"] = DesignTokens.FontSizeBody;
        dictionary["Theme.FontSize.Subtitle"] = DesignTokens.FontSizeSubtitle;
        dictionary["Theme.FontSize.Title"] = DesignTokens.FontSizeTitle;
        dictionary["Theme.FontSize.Display"] = DesignTokens.FontSizeDisplay;
        dictionary["Theme.FontSize.Clock"] = DesignTokens.FontSizeClock;
        dictionary["Theme.FontSize.Timer"] = DesignTokens.FontSizeTimer;

        dictionary["Theme.Motion.Fast"] = CreateDuration(DesignTokens.MotionFastMilliseconds, reduceMotion);
        dictionary["Theme.Motion.Normal"] = CreateDuration(DesignTokens.MotionNormalMilliseconds, reduceMotion);
        dictionary["Theme.Motion.Slow"] = CreateDuration(DesignTokens.MotionSlowMilliseconds, reduceMotion);
        dictionary["Theme.Motion.Drawer"] = CreateDuration(DesignTokens.MotionDrawerMilliseconds, reduceMotion);

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        easing.Freeze();
        dictionary["Theme.EaseOut"] = easing;

        dictionary["Theme.ReducedMotion"] = reduceMotion;
        dictionary["Theme.HighContrast"] = highContrast;
        dictionary["Theme.MicaOpacity"] = palette.MicaOpacity;
        dictionary["Theme.CardOpacity"] = palette.CardOpacity;

        return dictionary;
    }

    private static ThemePalette BuildHighContrastPalette(ThemePalette basePalette)
    {
        var window = ToHex(SystemColors.WindowColor);
        var windowText = ToHex(SystemColors.WindowTextColor);
        var highlight = ToHex(SystemColors.HighlightColor);
        var highlightText = ToHex(SystemColors.HighlightTextColor);

        return basePalette with
        {
            Accent = highlight,
            AccentSecondary = highlight,
            AccentSoft = highlight,
            OnAccent = highlightText,
            Wall = window,
            WallSecondary = window,
            MicaBase = window,
            MicaOpacity = 1,
            CardBase = window,
            CardOpacity = 1,
            CardSolid = window,
            Border = windowText,
            Line = windowText,
            Text = windowText,
            TextSecondary = windowText,
            TextTertiary = windowText,
            Hover = highlight,
            Active = highlight,
            Scrim = "#A0000000",
            ScrimSoft = "#50000000",
            UrgencyCalm = highlight,
            UrgencyWarm = windowText,
            UrgencyHot = windowText,
            UrgencyUrgent = highlight,
            UrgencyExpired = windowText,
            ShadowColor = windowText,
            ShadowOpacity = 0,
            ShadowBlurRadius = 0,
            ShadowDepth = 0,
            ShadowSoftColor = windowText,
            ShadowSoftOpacity = 0,
            ShadowSoftBlurRadius = 0,
            ShadowSoftDepth = 0,
        };
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush BuildTranslucentBrush(string hex, double opacity)
    {
        var color = ParseColor(hex);
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static readonly string[] WidgetSkinKeys = ["Opaque", "Transparent", "Acrylic", "LiquidGlass"];

    /// <summary>
    /// 四套小组件皮肤：不透明纯色、半透明、磨砂渐变（亚克力）与带高光的玻璃。
    /// 高对比度下统一回到系统色实心卡片，保证可读性。
    /// </summary>
    private static void AddWidgetSkinResources(
        ResourceDictionary dictionary,
        ThemePalette palette,
        bool highContrast)
    {
        var card = ParseColor(palette.CardSolid);
        var text = ParseColor(palette.Text);

        if (highContrast)
        {
            foreach (var key in WidgetSkinKeys)
            {
                dictionary[$"Theme.WidgetSkin.{key}.SurfaceBrush"] = CreateBrush(palette.CardSolid);
                dictionary[$"Theme.WidgetSkin.{key}.BorderBrush"] = CreateBrush(palette.Text);
                dictionary[$"Theme.WidgetSkin.{key}.BorderThickness"] = new Thickness(DesignTokens.StrokeThick);
            }

            return;
        }

        dictionary["Theme.WidgetSkin.Opaque.SurfaceBrush"] = CreateBrush(palette.CardSolid);
        dictionary["Theme.WidgetSkin.Opaque.BorderBrush"] = CreateBrush(palette.Border);
        dictionary["Theme.WidgetSkin.Opaque.BorderThickness"] = new Thickness(DesignTokens.StrokeHairline);

        dictionary["Theme.WidgetSkin.Transparent.SurfaceBrush"] = CreateBrush(WithAlpha(card, 0xCC));
        dictionary["Theme.WidgetSkin.Transparent.BorderBrush"] = CreateBrush(WithAlpha(text, 0x30));
        dictionary["Theme.WidgetSkin.Transparent.BorderThickness"] = new Thickness(DesignTokens.StrokeHairline);

        dictionary["Theme.WidgetSkin.Acrylic.SurfaceBrush"] = BuildAcrylicSurface(card);
        dictionary["Theme.WidgetSkin.Acrylic.BorderBrush"] = CreateBrush(WithAlpha(text, 0x3D));
        dictionary["Theme.WidgetSkin.Acrylic.BorderThickness"] = new Thickness(DesignTokens.StrokeHairline);

        dictionary["Theme.WidgetSkin.LiquidGlass.SurfaceBrush"] = BuildLiquidGlassSurface(card);
        dictionary["Theme.WidgetSkin.LiquidGlass.BorderBrush"] = BuildGlassRim(card);
        dictionary["Theme.WidgetSkin.LiquidGlass.BorderThickness"] = new Thickness(DesignTokens.StrokeThin);
    }

    /// <summary>
    /// 亚克力：中等不透明度的斜向渐变。真实模糊由抓屏层提供，
    /// 这里只负责压住背景以保证文字可读，透明度低于纯色卡面。
    /// </summary>
    private static Brush BuildAcrylicSurface(Color card)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.72, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(card, 0x9A), 0),
                new GradientStop(WithAlpha(card, 0x8E), 0.58),
                new GradientStop(WithAlpha(card, 0x94), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 液态玻璃：低不透明度玻璃叠加左上角镜面高光、底部反光与冷色偏移，
    /// 让背后的模糊画面明显透出，形成 Token Monitor 那种“有厚度的玻璃板”。
    /// </summary>
    private static Brush BuildLiquidGlassSurface(Color card)
    {
        var group = new DrawingGroup();

        var baseBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(WithAlpha(card, 0x78), 0),
                new GradientStop(WithAlpha(card, 0x80), 0.62),
                new GradientStop(WithAlpha(card, 0x7C), 1),
            },
        };
        group.Children.Add(new GeometryDrawing(
            baseBrush,
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        // 左上镜面高光：范围大而淡，只让上缘比下缘亮一点，避免整块发白。
        var sheen = new RadialGradientBrush
        {
            Center = new Point(0.18, -0.1),
            GradientOrigin = new Point(0.18, -0.1),
            RadiusX = 0.95,
            RadiusY = 0.9,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        group.Children.Add(new GeometryDrawing(
            sheen,
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 玻璃边缘：上缘一道亮线、下缘一道极淡反光，模拟玻璃板的折射边。
    /// 深色主题用白光，浅色主题用暗线，避免白边在白底上消失。
    /// </summary>
    private static Brush BuildGlassRim(Color card)
    {
        var dark = ((0.299 * card.R) + (0.587 * card.G) + (0.114 * card.B)) < 110;
        var rim = dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x10, 0x14, 0x1A);
        var strong = dark ? (byte)0x3A : (byte)0x20;
        var weak = dark ? (byte)0x08 : (byte)0x04;
        var medium = dark ? (byte)0x1E : (byte)0x0E;

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.15, 0),
            EndPoint = new Point(0.85, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(strong, rim.R, rim.G, rim.B), 0),
                new GradientStop(Color.FromArgb(weak, rim.R, rim.G, rim.B), 0.55),
                new GradientStop(Color.FromArgb(medium, rim.R, rim.G, rim.B), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static Brush BuildWallBrush(ThemePalette palette)
    {
        var wall = ParseColor(palette.Wall);
        var wallSecondary = ParseColor(palette.WallSecondary);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(wall, 0),
                new GradientStop(wallSecondary, 0.55),
                new GradientStop(wall, 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static Brush BuildGlowBrush(ThemePalette palette, bool highContrast)
    {
        if (highContrast)
        {
            return Brushes.Transparent;
        }

        var accentSoft = ParseColor(palette.AccentSoft);
        var transparent = Color.FromArgb(0, accentSoft.R, accentSoft.G, accentSoft.B);
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.86, 0.08),
            GradientOrigin = new Point(0.86, 0.08),
            RadiusX = 1,
            RadiusY = 1,
            GradientStops =
            {
                new GradientStop(accentSoft, 0),
                new GradientStop(transparent, 0.72),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static DropShadowEffect BuildShadow(string hex, double opacity, double blurRadius, double depth)
    {
        var effect = new DropShadowEffect
        {
            Color = ParseColor(hex),
            Opacity = Math.Clamp(opacity, 0, 1),
            BlurRadius = Math.Max(0, blurRadius),
            ShadowDepth = Math.Max(0, depth),
            Direction = 270,
            RenderingBias = RenderingBias.Performance,
        };
        effect.Freeze();
        return effect;
    }

    private static Duration CreateDuration(int milliseconds, bool reduceMotion) =>
        new(reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds));

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>给基色叠加 alpha，用于由文字色派生描边与滚动条。</summary>
    private static string WithAlpha(string hex, byte alpha)
    {
        var color = ParseColor(hex);
        return $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>按比例把基色向目标色混合，得到“比卡片略深/略浅”的输入框底色。</summary>
    private static string Blend(string baseHex, string targetHex, double amount)
    {
        var from = ParseColor(baseHex);
        var to = ParseColor(targetHex);
        var ratio = Math.Clamp(amount, 0, 1);
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * ratio);

        return $"#FF{Mix(from.R, to.R):X2}{Mix(from.G, to.G):X2}{Mix(from.B, to.B):X2}";
    }
}
