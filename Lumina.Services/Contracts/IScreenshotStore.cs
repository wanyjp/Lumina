using Lumina.Domain.Models;
using Lumina.Services.Configuration;

namespace Lumina.Services.Contracts;

/// <summary>
/// 截图历史与存储。实现于 P2-07（Data 层，依赖本地文件与 SQLite）。
/// <para>
/// 路径统一以相对 <c>files/</c> 的字符串保存；<see cref="Save"/> 在磁盘不足、
/// 写入失败或缩略图生成失败时抛出可读异常，由调用方提示用户。
/// </para>
/// </summary>
public interface IScreenshotStore
{
    string StorageDirectory { get; }

    ScreenshotRecord Save(
        byte[] pngBytes,
        string captureType,
        int width,
        int height,
        DateTimeOffset capturedAt,
        bool? keepOriginal = null);

    IReadOnlyList<ScreenshotRecord> Recent(int? limit = null);

    bool Delete(long id);

    /// <summary>按上限淘汰最旧记录；不传上限时读取用户配置的保留张数。</summary>
    int EnforceLimit(int? limit = null);

    /// <summary>当前生效的保留张数（用户配置，越界回落到默认值）。</summary>
    int RetentionLimit();

    ScreenshotReconcileReport Reconcile();

    string ResolvePath(ScreenshotRecord record);

    string ResolvePath(string relativePath);
}
