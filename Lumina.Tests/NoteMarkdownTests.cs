using Lumina.Services.Notes;

namespace Lumina.Tests;

/// <summary>
/// P3-05 Markdown 预览：只认白名单语法；原始 HTML 与未闭合标记按普通文字处理。
/// </summary>
public class NoteMarkdownTests
{
    [Fact]
    public void Paragraph_keeps_bold_and_inline_code()
    {
        var blocks = NoteMarkdown.Parse("今天要 **先写周报**，再看 `CountdownCalendar`。");

        var block = Assert.Single(blocks);
        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Collection(
            block.Inlines,
            inline =>
            {
                Assert.Equal(MarkdownInlineKind.Text, inline.Kind);
                Assert.Equal("今天要 ", inline.Text);
            },
            inline =>
            {
                Assert.Equal(MarkdownInlineKind.Bold, inline.Kind);
                Assert.Equal("先写周报", inline.Text);
            },
            inline =>
            {
                Assert.Equal(MarkdownInlineKind.Text, inline.Kind);
                Assert.Equal("，再看 ", inline.Text);
            },
            inline =>
            {
                Assert.Equal(MarkdownInlineKind.Code, inline.Kind);
                Assert.Equal("CountdownCalendar", inline.Text);
            },
            inline =>
            {
                Assert.Equal(MarkdownInlineKind.Text, inline.Kind);
                Assert.Equal("。", inline.Text);
            });
    }

    [Fact]
    public void Bullet_and_ordered_lists_are_recognized()
    {
        var blocks = NoteMarkdown.Parse("- 第一项\n1. 第一步\n2) 第二步\n3、第三步");

        Assert.Collection(
            blocks,
            block => Assert.Equal(MarkdownBlockKind.Bullet, block.Kind),
            block =>
            {
                Assert.Equal(MarkdownBlockKind.Ordered, block.Kind);
                Assert.Equal(1, block.Number);
            },
            block =>
            {
                Assert.Equal(MarkdownBlockKind.Ordered, block.Kind);
                Assert.Equal(2, block.Number);
            },
            block =>
            {
                Assert.Equal(MarkdownBlockKind.Ordered, block.Kind);
                Assert.Equal(3, block.Number);
            });
    }

    [Fact]
    public void Checklist_blocks_report_checked_state()
    {
        var blocks = NoteMarkdown.Parse("☑ 已完成\n[ ] 未完成\n- [x] 兼容写法");

        Assert.Equal(MarkdownBlockKind.Check, blocks[0].Kind);
        Assert.True(blocks[0].Checked);
        Assert.Equal(MarkdownBlockKind.Check, blocks[1].Kind);
        Assert.False(blocks[1].Checked);
        Assert.Equal(MarkdownBlockKind.Bullet, blocks[2].Kind);
    }

    [Fact]
    public void Code_block_is_kept_raw_and_not_inline_parsed()
    {
        var blocks = NoteMarkdown.Parse("```\n**不是加粗**\n`也不是代码`\n```");

        var block = Assert.Single(blocks);
        Assert.Equal(MarkdownBlockKind.Code, block.Kind);
        Assert.Single(block.Inlines);
        Assert.Equal("**不是加粗**\n`也不是代码`", block.Inlines[0].Text);
    }

    [Fact]
    public void Raw_html_is_plain_text()
    {
        var blocks = NoteMarkdown.Parse("<b>粗体</b> 与 <script>alert(1)</script>");

        var block = Assert.Single(blocks);
        Assert.Single(block.Inlines);
        Assert.Equal(MarkdownInlineKind.Text, block.Inlines[0].Kind);
        Assert.Equal("<b>粗体</b> 与 <script>alert(1)</script>", block.Inlines[0].Text);
    }

    [Fact]
    public void Unclosed_markers_stay_literal()
    {
        var blocks = NoteMarkdown.Parse("**没闭合 和 `没闭合");

        var block = Assert.Single(blocks);
        Assert.Single(block.Inlines);
        Assert.Equal("**没闭合 和 `没闭合", block.Inlines[0].Text);
    }

    [Fact]
    public void Blank_lines_split_paragraphs_and_crlf_is_supported()
    {
        var blocks = NoteMarkdown.Parse("第一段\r\n第二行\r\n\r\n第二段");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("第一段\n第二行", blocks[0].Inlines[0].Text);
        Assert.Equal("第二段", blocks[1].Inlines[0].Text);
    }

    [Fact]
    public void Empty_content_yields_no_blocks()
    {
        Assert.Empty(NoteMarkdown.Parse(null));
        Assert.Empty(NoteMarkdown.Parse("   "));
    }
}
