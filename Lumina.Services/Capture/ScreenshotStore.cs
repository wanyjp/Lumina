using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Capture;

/// <summary>
/// 截图存储：原图/缩略图落盘、元数据入库、50 张上限淘汰与缺失文件对账。
/// <para>
/// 一致性准则：先写文件再写库，写库失败回滚刚写的文件；删除时先删记录再删文件，
/// 文件删除失败留给下次对账处理，避免“记录还在但文件消失”反复报错。
/// </para>
/// </summary>
public sealed class ScreenshotStore : IScreenshotStore
{
    private const long MinimumFreeBytes = 50L * 1024 * 1024;

    private readonly ScreenshotFileService _files;
    private readonly IScreenshotCatalog _catalog;
    private readonly ISettingsStore _settings;
    private readonly Func<byte[], byte[]>? _thumbnailFactory;
    private readonly string _filesDirectory;

    public ScreenshotStore(
        ScreenshotFileService files,
        IScreenshotCatalog catalog,
        ISettingsStore settings,
        Func<byte[], byte[]>? thumbnailFactory = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);

        _files = files;
        _catalog = catalog;
        _settings = settings;
        _thumbnailFactory = thumbnailFactory;
        _filesDirectory = Path.GetFullPath(Path.Combine(files.StorageDirectory, ".."));
    }

    public string StorageDirectory => _files.StorageDirectory;

    /// <summary>
    /// 保存截图并入库。磁盘空间不足、写入失败或缩略图生成失败都会抛出可读异常，
    /// 由调用方提示用户，不静默丢弃。
    /// </summary>
    public ScreenshotRecord Save(
        byte[] pngBytes,
        string captureType,
        int width,
        int height,
        DateTimeOffset capturedAt,
        bool? keepOriginal = null)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureType);

        EnsureFreeSpace();

        var keep = keepOriginal
            ?? !_settings.Get(PreferenceKeys.ScreenshotThumbOnly, PreferenceDefaults.ScreenshotThumbOnly);
        var originalPath = _files.SavePng(pngBytes, capturedAt);
        var thumbnailPath = WriteThumbnail(originalPath, pngBytes);
        var storedPath = originalPath;

        if (!keep)
        {
            if (thumbnailPath is null)
            {
                TryDelete(originalPath);
                throw new IOException("缩略图生成失败，已取消本次保存。");
            }

            TryDelete(originalPath);
            storedPath = thumbnailPath;
        }

        var record = new ScreenshotRecord(
            Id: 0,
            CaptureType: captureType,
            FilePath: ToRelative(storedPath),
            ThumbPath: thumbnailPath is null ? null : ToRelative(thumbnailPath),
            OcrText: null,
            Width: width,
            Height: height,
            CreatedAt: capturedAt);

        long id;
        try
        {
            id = _catalog.Add(record);
        }
        catch
        {
            TryDelete(storedPath);
            if (thumbnailPath is not null)
            {
                TryDelete(thumbnailPath);
            }

            throw;
        }

        EnforceLimit();
        return record with { Id = id };
    }

    public IReadOnlyList<ScreenshotRecord> Recent(int? limit = null) =>
        _catalog.Recent(Math.Max(1, limit ?? RetentionLimit()));

    /// <summary>用户配置的保留张数；越界或缺失时回落到 50 张。</summary>
    public int RetentionLimit()
    {
        var configured = _settings.Get(
            PreferenceKeys.ScreenshotRetentionLimit,
            PreferenceDefaults.ScreenshotRetentionLimit);

        return Math.Clamp(
            configured,
            AppLimits.ScreenshotRetentionMin,
            AppLimits.ScreenshotRetentionMax);
    }

    /// <summary>删除记录与对应文件；记录不存在返回 false。</summary>
    public bool Delete(long id)
    {
        var record = _catalog.Find(id);
        if (record is null)
        {
            return false;
        }

        _catalog.Delete(id);
        TryDelete(ResolvePath(record.FilePath));

        if (!string.IsNullOrWhiteSpace(record.ThumbPath)
            && !string.Equals(record.FilePath, record.ThumbPath, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(ResolvePath(record.ThumbPath));
        }

        return true;
    }

    /// <summary>按上限淘汰最旧记录，返回删除数量；不传上限时读取用户配置（默认 50 张）。</summary>
    public int EnforceLimit(int? limit = null)
    {
        var safeLimit = Math.Max(1, limit ?? RetentionLimit());
        var overflow = _catalog.Count() - safeLimit;
        if (overflow <= 0)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var record in _catalog.Oldest(overflow))
        {
            if (Delete(record.Id))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>启动对账：文件已不存在的记录直接清理，返回清理数量。</summary>
    public ScreenshotReconcileReport Reconcile()
    {
        var missing = 0;
        foreach (var record in _catalog.All())
        {
            if (File.Exists(ResolvePath(record.FilePath)))
            {
                continue;
            }

            _catalog.Delete(record.Id);
            missing++;
        }

        return new ScreenshotReconcileReport(missing);
    }

    public string ResolvePath(ScreenshotRecord record) => ResolvePath(record.FilePath);

    public string ResolvePath(string relativePath) =>
        Path.Combine(_filesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void EnsureFreeSpace()
    {
        Directory.CreateDirectory(_files.StorageDirectory);

        var root = Path.GetPathRoot(Path.GetFullPath(_files.StorageDirectory));
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < MinimumFreeBytes)
        {
            throw new IOException(
                $"磁盘可用空间不足 50 MB（{drive.Name}），已取消保存截图。请清理磁盘后重试。");
        }
    }

    private string? WriteThumbnail(string originalPath, byte[] pngBytes)
    {
        if (_thumbnailFactory is null)
        {
            return null;
        }

        var bytes = _thumbnailFactory(pngBytes);
        if (bytes.Length == 0)
        {
            return null;
        }

        var directory = Path.Combine(_files.StorageDirectory, "thumbs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(originalPath)}_thumb.png");

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string ToRelative(string absolutePath)
    {
        var relative = Path.GetRelativePath(_filesDirectory, Path.GetFullPath(absolutePath));
        return relative.Replace('\\', '/');
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 文件被占用：留给下次对账。
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足：留给下次对账。
        }
    }
}
