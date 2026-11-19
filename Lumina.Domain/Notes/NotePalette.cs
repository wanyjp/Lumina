namespace Lumina.Domain.Notes;

/// <summary>便签色卡：稳定键落库，界面只负责把键映射成画刷。</summary>
public sealed record NoteColor(string Key, string Name, string Hex);

/// <summary>
/// 便签颜色目录。落库用稳定键（amber/rose/sky/mint/violet/slate），
/// 原型里的色值或名称在导入/读取时归一到这里；未知值回落默认色。
/// </summary>
public static class NotePalette
{
    public const string DefaultKey = "amber";

    public static IReadOnlyList<NoteColor> All { get; } =
    [
        new(DefaultKey, "琥珀", "#E8B45A"),
        new("rose", "玫瑰", "#E4868C"),
        new("sky", "天蓝", "#6FA8DC"),
        new("mint", "薄荷", "#6FC2A0"),
        new("violet", "紫罗兰", "#9C8AD6"),
        new("slate", "石墨", "#8A9199"),
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultKey;
        }

        var trimmed = value.Trim();
        foreach (var color in All)
        {
            if (string.Equals(color.Key, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(color.Hex, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(color.Name, trimmed, StringComparison.Ordinal))
            {
                return color.Key;
            }
        }

        return DefaultKey;
    }

    public static NoteColor Resolve(string? key) =>
        All.FirstOrDefault(color => string.Equals(color.Key, Normalize(key), StringComparison.Ordinal))
        ?? All[0];
}
