using System.IO;
using Lumina.Data.Backup;
using Lumina.Data.Migrations;
using Lumina.Services.Backup;

namespace Lumina.App.Backup;

/// <summary>
/// 备份协调器的应用层实现：导出走 SQLite 在线快照，恢复只写“待恢复请求”，
/// 由下次启动在打开数据库之前应用，避免运行期替换正在使用的库文件。
/// </summary>
internal sealed class AppBackupCoordinator : IBackupCoordinator
{
    private readonly DatabaseBackupService _backups;
    private readonly PendingRestoreStore _pendingStore;

    public AppBackupCoordinator(DatabaseBackupService backups, PendingRestoreStore pendingStore)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(pendingStore);

        _backups = backups;
        _pendingStore = pendingStore;
    }

    public string BackupDirectory => _backups.BackupDirectory;

    public PendingRestoreInfo? PendingRestore
    {
        get
        {
            var request = _pendingStore.Read();
            return request is null
                ? null
                : new PendingRestoreInfo(request.BackupPath, request.RequestedAt);
        }
    }

    public Task<BackupExportResult> ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _backups.BackupTo(path);

                var validation = _backups.Validate(path);
                var size = new FileInfo(path).Length;
                return new BackupExportResult(path, size, validation.SchemaVersion);
            },
            cancellationToken);
    }

    public Task<BackupValidation> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = _backups.Validate(path);
                if (!result.IsValid)
                {
                    return new BackupValidation(false, result.SchemaVersion, result.Error);
                }

                if (result.SchemaVersion > MigrationCatalog.LatestVersion)
                {
                    return new BackupValidation(
                        false,
                        result.SchemaVersion,
                        $"备份来自更高版本（schema v{result.SchemaVersion}），请先升级拾光再恢复。");
                }

                return new BackupValidation(true, result.SchemaVersion, null);
            },
            cancellationToken);
    }

    public Task QueueRestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        _pendingStore.Write(path);
        return Task.CompletedTask;
    }

    public void CancelPendingRestore() => _pendingStore.Clear();
}
