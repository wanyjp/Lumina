using System.Windows.Controls;
using Lumina.Domain.Passwords;

namespace Lumina.UI.Wpf.Vault;

/// <summary>把 <see cref="PasswordStrength"/> 结果映射到四段强度条与文案。</summary>
internal static class PasswordStrengthVisuals
{
    private static readonly string[] ScoreBrushes =
    [
        "Theme.Urgency.HotBrush",
        "Theme.Urgency.WarmBrush",
        "Theme.AccentBrush",
        "Theme.Urgency.CalmBrush",
    ];

    public static void Apply(
        Border[] segments,
        TextBlock label,
        string? password,
        bool hidden = false)
    {
        if (hidden)
        {
            Fill(segments, 0, null);
            label.Text = "已隐藏";
            return;
        }

        var result = PasswordStrength.Evaluate(password);
        var brush = result.Score > 0 ? ScoreBrushes[result.Score - 1] : null;
        Fill(segments, result.FilledSegments, brush);
        label.Text = result.Label;
    }

    private static void Fill(Border[] segments, int count, string? brushKey)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i].SetResourceReference(
                Border.BackgroundProperty,
                i < count && brushKey is not null ? brushKey : "Theme.LineBrush");
        }
    }
}
