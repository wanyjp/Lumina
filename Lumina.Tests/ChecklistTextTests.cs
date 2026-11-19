using Lumina.Services.Notes;

namespace Lumina.Tests;

/// <summary>
/// P3-05 清单文本：整段转清单、剥离标记、`[ ]/[x]` 归一与混合内容逐行判断。
/// </summary>
public class ChecklistTextTests
{
    [Fact]
    public void To_checklist_prefixes_every_non_empty_line()
    {
        var result = ChecklistText.ToChecklist("买菜\n\n做饭");

        Assert.Equal("☐ 买菜\n\n☐ 做饭", result);
    }

    [Fact]
    public void To_checklist_normalizes_existing_markers_instead_of_double_prefixing()
    {
        var result = ChecklistText.ToChecklist("[x] 已完成\n[ ] 未完成\n☑ 已勾选");

        Assert.Equal("☑ 已完成\n☐ 未完成\n☑ 已勾选", result);
    }

    [Fact]
    public void To_plain_text_strips_markers_and_keeps_indentation()
    {
        var result = ChecklistText.ToPlainText("☐ 第一项\n  ☑ 子项\n普通行");

        Assert.Equal("第一项\n  子项\n普通行", result);
    }

    [Fact]
    public void Normalize_unifies_bracket_and_glyph_markers()
    {
        var result = ChecklistText.Normalize("[ ] a\n[x] b\n[X] c\n[√] d\n☐ e\n☑ f");

        Assert.Equal("☐ a\n☑ b\n☑ c\n☑ d\n☐ e\n☑ f", result);
    }

    [Fact]
    public void Mixed_content_is_judged_line_by_line()
    {
        const string content = "标题行\n- 普通列表\n[ ] 待办\n说明";

        Assert.Equal("☐ 标题行\n☐ - 普通列表\n☐ 待办\n☐ 说明", ChecklistText.ToChecklist(content));
        Assert.Equal("标题行\n- 普通列表\n待办\n说明", ChecklistText.ToPlainText(content));
    }

    [Fact]
    public void Count_reports_total_and_done()
    {
        var (total, done) = ChecklistText.Count("☑ a\n☐ b\n普通\n[ ] c");

        Assert.Equal(3, total);
        Assert.Equal(1, done);
    }

    [Fact]
    public void Try_toggle_flips_only_the_target_line()
    {
        var toggled = ChecklistText.TryToggle("☐ 买菜\n☑ 做饭\n普通行", 0, out var updated);

        Assert.True(toggled);
        Assert.Equal("☑ 买菜\n☑ 做饭\n普通行", updated);
    }

    [Fact]
    public void Try_toggle_rejects_non_checklist_lines()
    {
        Assert.False(ChecklistText.TryToggle("普通行", 0, out var unchanged));
        Assert.Equal("普通行", unchanged);
        Assert.False(ChecklistText.TryToggle("☐ a", 5, out _));
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, ChecklistText.ToChecklist(string.Empty));
        Assert.Equal(string.Empty, ChecklistText.ToPlainText(null));
        Assert.Equal((0, 0), ChecklistText.Count(null));
    }
}
