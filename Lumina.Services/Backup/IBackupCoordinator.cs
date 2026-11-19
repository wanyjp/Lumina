namespace Lumina.Services.Backup;

/// <summary>备份文件校验结果（UI 与协调器共用的轻量视图）。</summary>
public sealed record BackupValidation(bool IsValid, int SchemaVersion, string? Error);

/// <summary>一次导出结果：实际写入路径、文件大小与 schema 版本。</summary>
public sealed record BackupExportResult(string Path, long SizeBytes, int SchemaVersion);

/// <summary>已排队、等待下次启动应用的恢复请求。</summary>
public sealed record PendingRestoreInfo(string BackupPath, DateTimeOffset RequestedAt);

/// <summary>
/// 备份导出与恢复的应用层协调器：UI 只负责选文件与确认，
/// 具体快照、校验、排队恢复由实现方处理。
/// </summary>
public interface IBackupCoordinator
{
    /// <summary>应用自己的备份目录（自动备份与手动导出默认落点）。</summary>
    string BackupDirectory { get; }

    /// <summary>当前排队的恢复请求；没有则为 null。</summary>
    PendingRestoreInfo? PendingRestore { get; }

    /// <summary>把当前库导出到指定路径；返回写入结果。</summary>
    Task<BackupExportResult> ExportAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>只读校验备份文件是否完整、schema 是否可识别。</summary>
    Task<BackupValidation> ValidateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 排队一次恢复：写入恢复请求文件，**不立即改动当前库**；
    /// 下次启动在打开数据库之前应用，并自动生成 pre-restore 快照。
    /// </summary>
    Task QueueRestoreAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>取消尚未应用的恢复请求。</summary>
    void CancelPendingRestore();
}
