using Lumina.Domain.Enums;

namespace Lumina.Services.Configuration;

/// <summary>单个皮肤选项：值、稳定键、显示名与说明。</summary>
public sealed record WidgetSkinOption(
    WidgetSkin Value,
    string Key,
    string Label,
    string Description);

/// <summary>
/// 小组件皮肤目录（P3-04 反馈修复）：设置中心、模块抽屉与组件窗口共用同一套顺序与文案。
/// 皮肤只改变卡片表面材质，不改变尺寸、拖动、吸附与点击穿透。
/// </summary>
public static class WidgetSkinCatalog
{
    public const string OpaqueKey = "opaque";
    public const string TransparentKey = "transparent";
    public const string AcrylicKey = "acrylic";
    public const string LiquidGlassKey = "liquidGlass";

    public static IReadOnlyList<WidgetSkinOption> All { get; } = new[]
    {
        new WidgetSkinOption(
            WidgetSkin.Opaque,
            OpaqueKey,
            "不透明",
            "纯色卡片，桌面背景完全不透出，文字对比度最高。"),
        new WidgetSkinOption(
            WidgetSkin.Transparent,
            TransparentKey,
            "透明",
            "不模糊，直接透出桌面；保留 80% 底色，观感最轻盈。"),
        new WidgetSkinOption(
            WidgetSkin.Acrylic,
            AcrylicKey,
            "亚克力",
            "抓取组件背后的桌面并实时模糊，叠加磨砂底色与细描边，接近系统亚克力。"),
        new WidgetSkinOption(
            WidgetSkin.LiquidGlass,
            LiquidGlassKey,
            "液态玻璃",
            "模糊背景叠加斜向高光与更亮描边，透出更多桌面颜色。"),
    };

    public static WidgetSkinOption For(WidgetSkin value) =>
        All.FirstOrDefault(option => option.Value == value) ?? All[2];

    public static string KeyOf(WidgetSkin value) => For(value).Key;

    public static string LabelOf(WidgetSkin value) => For(value).Label;

    /// <summary>解析稳定键；未知值回落到亚克力，保证旧配置与手写值不破坏启动。</summary>
    public static WidgetSkin Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return WidgetSkin.Acrylic;
        }

        var trimmed = key.Trim();
        foreach (var option in All)
        {
            if (string.Equals(option.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return option.Value;
            }
        }

        return Enum.TryParse<WidgetSkin>(trimmed, ignoreCase: true, out var parsed)
            ? parsed
            : WidgetSkin.Acrylic;
    }

    /// <summary>枚举兜底：非法值统一按亚克力处理。</summary>
    public static WidgetSkin Normalize(WidgetSkin value) =>
        Enum.IsDefined(value) ? value : WidgetSkin.Acrylic;
}
