using System.Globalization;
using System.IO;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clipboard;

/// <summary>
/// 剪贴板保留策略编排：读取/钳制上限、超限淘汰、固定与取消固定、
/// 软删/硬删与过期软删清理；附件删除失败由启动时的孤儿文件对账兜底。
/// </summary>
public sealed class ClipboardRetentionService
{
    /// <summary>软删记录保留时长；超过后由启动清理永久移除。</summary>
    public static readonly TimeSpan SoftDeleteRetention = TimeSpan.FromDays(30);

    private readonly IClipboardStore _store;
    private readonly ISettingsStore _settings;

    public ClipboardRetentionService(IClipboardStore store, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        _store = store;
        _settings = settings;
    }

    /// <summary>当前生效上限，已钳制到 0–9999；0 表示不限制。</summary>
    public int Limit => EvictionPolicy.ClampLimit(
        _settings.Get(
            PreferenceKeys.ClipboardRetentionLimit,
            PreferenceDefaults.ClipboardRetentionLimit));

    /// <summary>应用新的保留上限并立即按新值清理，返回本次清理条数。</summary>
    public int SetLimit(int limit)
    {
        var clamped = EvictionPolicy.ClampLimit(limit);
        _settings.Set(PreferenceKeys.ClipboardRetentionLimit, clamped, module: "clipboard");
        return Trim();
    }

    /// <summary>按当前上限淘汰最旧的非固定记录并删除图片附件，返回清理条数。</summary>
    public int Trim()
    {
        var evicted = _store.EvictOldestNonPinned(Limit);
        DeleteImages(evicted);
        return evicted.Count;
    }

    /// <summary>清理超过软删保留期的记录与附件，返回清理条数。</summary>
    public int PurgeSoftDeleted(DateTimeOffset now)
    {
        var cutoff = now - SoftDeleteRetention;
        var purged = _store.PurgeSoftDeletedBefore(cutoff);
        DeleteImages(purged);
        return purged.Count;
    }

    /// <summary>固定条目：写入固定时间用于稳定排序，不参与上限计数与淘汰。</summary>
    public bool Pin(long id, DateTimeOffset? now = null) =>
        _store.SetPinned(id, true, now ?? DateTimeOffset.Now);

    public bool Unpin(long id) => _store.SetPinned(id, false, DateTimeOffset.Now);

    /// <summary>软删：保留数据库记录与附件，列表中不再出现。</summary>
    public bool SoftDelete(long id, DateTimeOffset? now = null) =>
        _store.SoftDelete(id, now ?? DateTimeOffset.Now);

    /// <summary>硬删：移除记录并尽力删除图片附件。</summary>
    public bool HardDelete(long id)
    {
        var item = _store.HardDelete(id);
        if (item is null)
        {
            return false;
        }

        DeleteImages([item]);
        return true;
    }

    /// <summary>
    /// 删除条目所属的剪贴板图片附件（只删图片目录内的文件）。
    /// 供采集器在去重/合并/写入失败时统一走白名单，避免数据库异常路径误删用户文件。
    /// </summary>
    public void DeleteOwnedFiles(IEnumerable<ClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        DeleteImages(items);
    }

    private static void DeleteImages(IEnumerable<ClipboardItem> items)
    {
        foreach (var item in items)
        {
            if (item.Kind != ClipboardKind.Image)
            {
                continue;
            }

            DeleteIfOwned(item.FilePath);
            DeleteIfOwned(item.ThumbPath);
        }
    }

    /// <summary>只删除剪贴板图片目录内的文件，避免数据库异常路径误删用户文件。</summary>
    private static void DeleteIfOwned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var root = Path.GetFullPath(AppPaths.ClipboardImagesDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            if (!string.Equals(
                    Path.GetDirectoryName(full),
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Delete(full);
        }
        catch (IOException)
        {
            // 文件被短暂占用，留待下次启动孤儿文件对账。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
        catch (ArgumentException)
        {
            // 非法路径直接跳过。
        }
    }

    /// <summary>格式化上限展示：0 显示为“不限制”。</summary>
    public static string DescribeLimit(int limit) =>
        EvictionPolicy.ClampLimit(limit) == 0
            ? "不限制"
            : $"{EvictionPolicy.ClampLimit(limit).ToString(CultureInfo.InvariantCulture)} 条";
}
