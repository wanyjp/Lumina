namespace Lumina.Domain.Models;

/// <summary>截图对账结果：文件已不存在、记录已清理的数量。</summary>
public sealed record ScreenshotReconcileReport(int MissingRecords);
