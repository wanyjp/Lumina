using Lumina.Domain.Enums;
using Lumina.Services.Clipboard;

namespace Lumina.Tests;

/// <summary>P2-01 剪贴板分类、归一化、哈希与隐私默认策略。</summary>
public class ClipboardClassifierTests
{
    [Fact]
    public void Classify_prioritizes_image_file_html_link_then_text()
    {
        Assert.Equal(
            ClipboardKind.Image,
            ClipClassifier.Classify(ClipboardSamples.Link, ClipboardSamples.Html, ClipboardSamples.Files, true));

        Assert.Equal(
            ClipboardKind.File,
            ClipClassifier.Classify(ClipboardSamples.Link, ClipboardSamples.Html, ClipboardSamples.Files, false));

        Assert.Equal(
            ClipboardKind.Html,
            ClipClassifier.Classify(ClipboardSamples.Link, ClipboardSamples.Html, null, false));

        Assert.Equal(
            ClipboardKind.Link,
            ClipClassifier.Classify(ClipboardSamples.Link, null, null, false));

        Assert.Equal(
            ClipboardKind.Text,
            ClipClassifier.Classify(ClipboardSamples.PlainText, null, null, false));
    }

    [Fact]
    public void NormalizeText_unifies_newlines_and_collapses_whitespace()
    {
        Assert.Equal(
            "第一行 内容\n\n第二行 结束",
            ClipClassifier.NormalizeText(ClipboardSamples.PlainText));
    }

    [Fact]
    public void Hash_is_stable_across_whitespace_variants()
    {
        var a = ClipClassifier.ComputeTextHash(
            ClipboardKind.Text,
            ClipClassifier.NormalizeText("hello   world\r\n"));
        var b = ClipClassifier.ComputeTextHash(
            ClipboardKind.Text,
            ClipClassifier.NormalizeText("  hello world  "));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_differs_by_kind_for_same_text()
    {
        var text = ClipClassifier.ComputeTextHash(ClipboardKind.Text, "example");
        var link = ClipClassifier.ComputeTextHash(ClipboardKind.Link, "example");

        Assert.NotEqual(text, link);
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://localhost:8080/path")]
    public void Web_link_detection_accepts_http_and_https(string value)
    {
        Assert.True(ClipClassifier.IsWebLink(value));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("https://example.com 带空格")]
    [InlineData("ftp://example.com")]
    [InlineData("not a link")]
    public void Web_link_detection_rejects_non_http_or_multiline_text(string value)
    {
        Assert.False(ClipClassifier.IsWebLink(value));
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)]
    [InlineData("12 3456", true)]
    [InlineData("123-456", true)]
    [InlineData("１２３４５６", true)]
    [InlineData("您的验证码是 830512，5 分钟内有效", true)]
    [InlineData("订单编号 20261115", false)]
    [InlineData("abcdef", false)]
    public void One_time_code_detection_matches_four_to_eight_digits(string value, bool expected)
    {
        Assert.Equal(expected, ClipboardPrivacyFilter.LooksLikeOneTimeCode(value));
    }

    [Theory]
    [InlineData("sk-proj-abcdefgh12345678", true)]
    [InlineData("ghp_1234567890abcdef", true)]
    [InlineData("github_pat_11ABCDEFG0123456789", true)]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature123", true)]
    [InlineData("AKIAIOSFODNN7EXAMPLE", true)]
    [InlineData("这是一段普通的十六个字符文本", false)]
    public void Access_token_detection_matches_known_prefixes(string value, bool expected)
    {
        Assert.Equal(expected, ClipboardPrivacyFilter.LooksLikeAccessToken(value));
    }

    [Theory]
    [InlineData("1Password.exe", true)]
    [InlineData("KeePassXC.exe", true)]
    [InlineData("bitwarden", true)]
    [InlineData("chrome.exe", false)]
    public void Password_manager_process_detection_is_case_insensitive(string value, bool expected)
    {
        Assert.Equal(expected, ClipboardPrivacyFilter.IsPasswordManagerProcess(value));
    }

    [Fact]
    public void Secret_formats_match_known_names()
    {
        Assert.True(ClipboardPrivacyFilter.IsSecretFormat("Clipboard Viewer Ignore"));
        Assert.True(ClipboardPrivacyFilter.IsSecretFormat("excludeclipboardcontentfrommonitorprocessing"));
        Assert.False(ClipboardPrivacyFilter.IsSecretFormat("UnicodeText"));
    }
}
