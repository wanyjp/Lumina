using Lumina.Services.Clipboard;

namespace Lumina.Tests;

/// <summary>P2-03 HTML 详情白名单清洗：只保留受限标签，脚本与外部资源一律剥离。</summary>
public class ClipboardHtmlSanitizerTests
{
    [Fact]
    public void Script_and_style_content_is_removed_entirely()
    {
        var html = "<p>前<script>alert('x')</script>中<style>.a{color:red}</style>后</p>";
        var plain = ClipboardHtmlSanitizer.ToPlainText(html);

        Assert.Equal("前中后", plain);
        Assert.DoesNotContain("alert", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("color", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Dangerous_containers_are_dropped_with_content()
    {
        var html = """
            <div>保留</div>
            <iframe src="https://evil.example/x"></iframe>
            <object data="x"></object>
            <svg><script>alert(1)</script></svg>
            <head><title>标题</title></head>
            <noscript>无脚本</noscript>
            <p>结束</p>
            """;

        var plain = ClipboardHtmlSanitizer.ToPlainText(html);

        Assert.Contains("保留", plain, StringComparison.Ordinal);
        Assert.Contains("结束", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("标题", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("无脚本", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_and_event_attributes_never_reach_output()
    {
        var html = "<p onclick=\"steal()\">正文<img src=\"https://cdn.example/a.png\" onerror=\"x\"/></p>";

        var model = ClipboardHtmlSanitizer.Sanitize(html);
        var safe = ClipboardHtmlSanitizer.ToSafeHtml(model);

        Assert.Single(model.Blocks);
        Assert.Equal("正文", model.ToPlainText());
        Assert.DoesNotContain("steal", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("onerror", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.example", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void Whitelisted_inline_styles_become_runs()
    {
        var html = "<p>普通<strong>加粗</strong><em>斜体</em><u>下划线</u><code>代码</code></p>";

        var model = ClipboardHtmlSanitizer.Sanitize(html);
        Assert.Single(model.Blocks);

        var runs = model.Blocks[0].Runs;
        Assert.Contains(runs, run => run.Text == "加粗" && run.Bold);
        Assert.Contains(runs, run => run.Text == "斜体" && run.Italic);
        Assert.Contains(runs, run => run.Text == "下划线" && run.Underline);
        Assert.Contains(runs, run => run.Text == "代码" && run.Code);
    }

    [Fact]
    public void Paragraphs_and_line_breaks_split_blocks()
    {
        var model = ClipboardHtmlSanitizer.Sanitize("<p>第一段</p><p>第二段<br/>换行</p>");

        Assert.Equal(3, model.Blocks.Count);
        Assert.Equal("第一段\n第二段\n换行", model.ToPlainText());
        Assert.All(model.Blocks, block => Assert.Equal(HtmlBlockKind.Paragraph, block.Kind));
    }

    [Fact]
    public void List_items_keep_kind_and_are_prefixed_in_plain_text()
    {
        var model = ClipboardHtmlSanitizer.Sanitize("<ul><li>甲</li><li>乙</li></ul>");

        Assert.Equal(2, model.Blocks.Count);
        Assert.All(model.Blocks, block => Assert.Equal(HtmlBlockKind.ListItem, block.Kind));
        Assert.Equal("· 甲\n· 乙", model.ToPlainText());
    }

    [Fact]
    public void Headings_and_quotes_are_preserved_as_blocks()
    {
        var model = ClipboardHtmlSanitizer.Sanitize("<h2>标题</h2><blockquote>引用</blockquote>");

        Assert.Equal(2, model.Blocks.Count);
        Assert.Equal(HtmlBlockKind.Heading, model.Blocks[0].Kind);
        Assert.Equal(2, model.Blocks[0].Level);
        Assert.Equal(HtmlBlockKind.Quote, model.Blocks[1].Kind);
    }

    [Fact]
    public void Entities_are_decoded()
    {
        var plain = ClipboardHtmlSanitizer.ToPlainText("<p>A &amp; B &lt;tag&gt; &quot;引号&quot; &#20320;&#x597D;</p>");

        Assert.Equal("A & B <tag> \"引号\" 你好", plain);
    }

    [Fact]
    public void Only_http_https_and_mailto_links_survive()
    {
        var html = """
            <p>
              <a href="https://example.com/a">安全</a>
              <a href="javascript:alert(1)">脚本</a>
              <a href="data:text/html;base64,AAA">数据</a>
              <a href="file:///C:/secret.txt">本地</a>
            </p>
            """;

        var model = ClipboardHtmlSanitizer.Sanitize(html);
        var runs = model.Blocks[0].Runs;

        var safe = Assert.Single(runs, run => run.Text == "安全");
        Assert.Equal("https://example.com/a", safe.Href);

        Assert.All(
            runs.Where(run => run.Text is "脚本" or "数据" or "本地"),
            run => Assert.Null(run.Href));

        var safeHtml = ClipboardHtmlSanitizer.ToSafeHtml(model);
        Assert.Contains("https://example.com/a", safeHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", safeHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data:", safeHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("file:///", safeHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void CfHtml_header_is_stripped_and_fragment_offsets_are_used()
    {
        const string fragment = "<b>片段</b>";
        var clipboardHtml = ClipboardHtmlSanitizer.BuildClipboardHtml(fragment);

        var extracted = ClipboardHtmlSanitizer.ExtractFragment(clipboardHtml);

        Assert.Equal(fragment, extracted);
        Assert.Equal("片段", ClipboardHtmlSanitizer.ToPlainText(clipboardHtml));
        Assert.DoesNotContain("Version", ClipboardHtmlSanitizer.ToPlainText(clipboardHtml), StringComparison.Ordinal);
    }

    [Fact]
    public void CfHtml_without_offsets_falls_back_to_first_tag()
    {
        const string raw = "Version:0.9\r\nStartHTML:0000000000\r\n<p>正文</p>";

        Assert.Equal("<p>正文</p>", ClipboardHtmlSanitizer.ExtractFragment(raw));
    }

    [Fact]
    public void Malformed_markup_does_not_throw()
    {
        var samples = new[]
        {
            "<p>未闭合",
            "</p>孤立闭合",
            "<",
            "&",
            "<p a=\"未闭合>文本</p>",
            "<!-- 未闭合注释",
        };

        foreach (var sample in samples)
        {
            var model = ClipboardHtmlSanitizer.Sanitize(sample);
            Assert.NotNull(model);
            Assert.False(model.Truncated);
        }
    }

    [Fact]
    public void Oversized_input_is_truncated_and_flagged()
    {
        var html = "<p>" + new string('字', ClipboardHtmlSanitizer.MaxInputLength) + "</p>";

        var model = ClipboardHtmlSanitizer.Sanitize(html);

        Assert.True(model.Truncated);
        Assert.NotEmpty(model.Blocks);
    }

    [Fact]
    public void Too_many_blocks_is_capped_and_flagged()
    {
        var html = string.Concat(
            Enumerable.Range(0, ClipboardHtmlSanitizer.MaxBlocks + 20).Select(index => $"<p>{index}</p>"));

        var model = ClipboardHtmlSanitizer.Sanitize(html);

        Assert.Equal(ClipboardHtmlSanitizer.MaxBlocks, model.Blocks.Count);
        Assert.True(model.Truncated);
    }

    [Fact]
    public void Safe_html_escapes_text_and_keeps_whitelisted_tags_only()
    {
        var model = ClipboardHtmlSanitizer.Sanitize("<p><strong>A &lt; B</strong></p><ul><li>项</li></ul>");

        var safe = ClipboardHtmlSanitizer.ToSafeHtml(model);

        Assert.Equal("<p><strong>A &lt; B</strong></p><li>项</li>", safe);
    }

    [Fact]
    public void Empty_input_yields_empty_model()
    {
        Assert.True(ClipboardHtmlSanitizer.Sanitize(null).IsEmpty);
        Assert.True(ClipboardHtmlSanitizer.Sanitize("   ").IsEmpty);
        Assert.Equal(string.Empty, ClipboardHtmlSanitizer.ToPlainText(null));
        Assert.Equal(string.Empty, ClipboardHtmlSanitizer.ToSafeHtml(HtmlDocumentModel.Empty));
    }
}
