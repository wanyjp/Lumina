using System.Globalization;
using System.Windows.Controls;

namespace Lumina.UI.Wpf.Controls;

/// <summary>
/// 时间选择下拉框的公共口径：可编辑下拉框，既提供 15 分钟步长建议，
/// 也允许直接输入任意 HH:mm；空值或「不设置（全天）」表示无时间。
/// 供日程编辑器与待办编辑器复用。
/// </summary>
public static class TimeChoice
{
    public const string NoTimeOption = "不设置（全天）";

    /// <summary>填充分钟步长建议；用户仍可在编辑框内输入任意时间。</summary>
    public static void Fill(ComboBox box)
    {
        var options = new List<string> { NoTimeOption };
        for (var minutes = 0; minutes < 24 * 60; minutes += 15)
        {
            options.Add($"{minutes / 60:00}:{minutes % 60:00}");
        }

        box.IsEditable = true;
        box.IsTextSearchEnabled = false;
        box.ItemsSource = options;
        box.SelectedIndex = 0;
        box.Text = NoTimeOption;

        // 自定义模板下选择项不会自动回写 Text；显式同步，保证下拉选择能在框内显示。
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string selected)
            {
                box.Text = selected;
            }
        };
    }

    /// <summary>选中指定时间；非 15 分钟倍数的时间直接写入编辑框，保证编辑不丢值。</summary>
    public static void Select(ComboBox box, TimeOnly? time)
    {
        if (time is null)
        {
            box.SelectedIndex = 0;
            box.Text = NoTimeOption;
            return;
        }

        var text = time.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (box.ItemsSource is List<string> options && options.Contains(text))
        {
            box.SelectedItem = text;
        }
        else
        {
            box.SelectedIndex = -1;
        }

        box.Text = text;
    }

    /// <summary>读取编辑框内容；空或不设置返回 null，非法格式返回 false 与提示。</summary>
    public static bool TryRead(ComboBox box, out TimeOnly? value, out string? error)
    {
        value = null;
        error = null;

        var text = (box.Text ?? string.Empty).Trim().Replace('：', ':');
        if (text.Length == 0 || text == NoTimeOption)
        {
            return true;
        }

        if (TimeOnly.TryParseExact(
                text,
                ["HH:mm", "H:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        error = "时间格式应为 HH:mm（例如 09:05），或留空表示不设置。";
        return false;
    }
}
