using Lumina.Domain.Ocr;

namespace Lumina.Tests;

/// <summary>P2-06 OCR 文本合并：顺序、空白行与空输入。</summary>
public class OcrTextComposerTests
{
    [Fact]
    public void Joins_lines_in_recognition_order()
    {
        var text = OcrTextComposer.Compose(["第一行", "Second line", "第三行"]);

        Assert.Equal("第一行\nSecond line\n第三行", text);
    }

    [Fact]
    public void Skips_blank_lines_and_trims_edges()
    {
        var text = OcrTextComposer.Compose(["  alpha  ", "", "   ", "beta"]);

        Assert.Equal("alpha\nbeta", text);
    }

    [Fact]
    public void Returns_empty_for_null_or_empty_input()
    {
        Assert.Equal(string.Empty, OcrTextComposer.Compose(null));
        Assert.Equal(string.Empty, OcrTextComposer.Compose([]));
        Assert.Equal(string.Empty, OcrTextComposer.Compose(["  ", "\t"]));
    }
}
