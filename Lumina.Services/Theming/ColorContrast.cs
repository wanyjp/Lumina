namespace Lumina.Services.Theming;

/// <summary>WCAG 相对亮度与对比度计算，用于主题色板验收。</summary>
public static class ColorContrast
{
    /// <summary>计算前景色与背景色的 WCAG 对比度（1–21）。8 位色值时忽略 Alpha。</summary>
    public static double Ratio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>计算 WCAG 相对亮度（0–1）。</summary>
    public static double RelativeLuminance(string hex)
    {
        var (red, green, blue) = ParseRgb(hex);
        return (0.2126 * Channel(red)) + (0.7152 * Channel(green)) + (0.0722 * Channel(blue));
    }

    private static double Channel(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static (byte Red, byte Green, byte Blue) ParseRgb(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        var value = hex.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length is not (6 or 8) || !value.All(Uri.IsHexDigit))
        {
            throw new FormatException($"无效的颜色值：{hex}");
        }

        var offset = value.Length == 8 ? 2 : 0;
        var red = Convert.ToByte(value.Substring(offset, 2), 16);
        var green = Convert.ToByte(value.Substring(offset + 2, 2), 16);
        var blue = Convert.ToByte(value.Substring(offset + 4, 2), 16);
        return (red, green, blue);
    }
}
