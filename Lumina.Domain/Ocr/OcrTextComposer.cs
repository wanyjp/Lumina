using System.Text;

namespace Lumina.Domain.Ocr;

/// <summary>
/// OCR 行合并：按识别顺序逐行输出，去掉空白行与行首尾空白。
/// 行内空格保留——OCR 引擎已按字符间距排版，额外压缩会破坏对齐。
/// </summary>
public static class OcrTextComposer
{
    public static string Compose(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line.Trim());
        }

        return builder.ToString();
    }
}
