using System.Globalization;
using System.IO;

namespace Lumina.Services.Capture;

/// <summary>
/// 截图原图落盘：P2-04 只负责写入 `files/screenshots` 并返回路径；
/// 元数据入库、缩略图与 50 张上限淘汰属 P2-07 的 ScreenshotStore 职责。
/// 文件名按捕获时刻生成，冲突时追加序号，保证同毫秒多次保存不覆盖。
/// </summary>
public sealed class ScreenshotFileService
{
    private readonly string _directory;

    public ScreenshotFileService()
        : this(Configuration.AppPaths.ScreenshotsDirectory)
    {
    }

    /// <summary>测试与隔离场景使用的显式目录构造。</summary>
    public ScreenshotFileService(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string StorageDirectory => _directory;

    /// <summary>
    /// 生成截图文件名：shot_yyyyMMdd_HHmmssfff.png。
    /// 按捕获时刻自带的偏移格式化（即捕获机器的墙上时钟），与执行机器时区无关，
    /// 保证跨时区环境（CI）下结果确定。
    /// </summary>
    public static string BuildFileName(DateTimeOffset capturedAt)
        => $"shot_{capturedAt.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture)}.png";

    /// <summary>
    /// 保存 PNG 字节并返回完整路径；目录不存在时自动创建。
    /// 写入失败（磁盘满/权限）抛出异常，由调用方降级提示，不静默丢弃。
    /// </summary>
    public string SavePng(byte[] pngBytes, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            throw new ArgumentException("PNG 内容为空，拒绝写入空文件。", nameof(pngBytes));
        }

        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, BuildFileName(capturedAt));
        var sequence = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(
                _directory,
                $"{Path.GetFileNameWithoutExtension(BuildFileName(capturedAt))}_{sequence}.png");
            sequence++;
        }

        File.WriteAllBytes(path, pngBytes);
        return path;
    }
}
