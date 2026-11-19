using System.Windows;
using System.Windows.Controls;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;

namespace Lumina.Widgets;

/// <summary>
/// 皮肤到主题资源的唯一映射：表面、描边、厚度与阴影全部走 <c>Theme.WidgetSkin.*</c>。
/// 使用动态资源引用，主题切换时已打开的组件同步换色，不需要重建窗口。
/// </summary>
internal static class WidgetSkinVisuals
{
    /// <summary>抓屏不可用时的回退：不透明卡面，保证文字始终可读。</summary>
    public static void ApplyFallback(Border card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.SetResourceReference(Border.BackgroundProperty, "Theme.CardSolidBrush");
    }

    public static void Apply(Border card, WidgetSkin skin)
    {
        ArgumentNullException.ThrowIfNull(card);

        // 资源键使用枚举名（PascalCase）；目录里的 Key 是配置稳定键（小写），不能直接拼资源键。
        var key = WidgetSkinCatalog.Normalize(skin).ToString();
        card.SetResourceReference(Border.BackgroundProperty, $"Theme.WidgetSkin.{key}.SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, $"Theme.WidgetSkin.{key}.BorderBrush");
        card.SetResourceReference(Border.BorderThicknessProperty, $"Theme.WidgetSkin.{key}.BorderThickness");

        // 用户明确要求去掉小组件外围阴影：只保留表面与描边，避免在桌面上形成“浮起”的暗圈。
        card.Effect = null;
    }
}
