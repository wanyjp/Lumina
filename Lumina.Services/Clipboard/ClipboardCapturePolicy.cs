namespace Lumina.Services.Clipboard;

/// <summary>剪贴板采集策略常量。</summary>
public static class ClipboardCapturePolicy
{
    /// <summary>同哈希内容在该时间窗内再次出现时合并为一条并前移。</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);

    /// <summary>单条文本上限，避免超大内容阻塞监听线程。</summary>
    public const int MaxTextLength = 1_000_000;

    /// <summary>单条 HTML 原始片段上限。</summary>
    public const int MaxHtmlLength = 5_000_000;

    /// <summary>缩略图最长边像素。</summary>
    public const int ThumbnailMaxEdge = 320;

    /// <summary>单张图片 DIB 载荷上限，超过则跳过，避免一次性读入超大对象。</summary>
    public const long MaxImageBytes = 32L * 1024 * 1024;
}
