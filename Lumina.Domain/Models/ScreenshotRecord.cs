namespace Lumina.Domain.Models;

/// <summary>
/// 截图元数据；路径统一相对 <c>files/</c> 保存，迁移数据目录或换机器后仍可解析。
/// </summary>
public sealed record ScreenshotRecord(
    long Id,
    string CaptureType,
    string FilePath,
    string? ThumbPath,
    string? OcrText,
    int Width,
    int Height,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// 是否保留了原图。开启“仅保留缩略图”时 <see cref="FilePath"/> 与
    /// <see cref="ThumbPath"/> 指向同一个缩略图文件。
    /// </summary>
    public bool HasOriginal =>
        !string.Equals(FilePath, ThumbPath, StringComparison.OrdinalIgnoreCase);
}
