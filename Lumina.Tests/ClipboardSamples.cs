namespace Lumina.Tests;

/// <summary>剪贴板格式样本集：覆盖文本、链接、HTML、文件与图片语义。</summary>
internal static class ClipboardSamples
{
    public const string PlainText = "  第一行\t内容  \r\n\r\n 第二行   结束  ";

    public const string Link = "https://example.com/docs?q=clipboard";

    public const string Html = "<div><b>示例 HTML</b><span>片段</span></div>";

    public static IReadOnlyList<string> Files { get; } =
        [@"C:\Temp\a.txt", @"C:\Temp\b.png"];
}
