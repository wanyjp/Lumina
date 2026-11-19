using Lumina.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Backup;

/// <summary>启动时应用待恢复请求的结果。</summary>
public sealed record PendingRestoreOutcome(
    bool HadRequest,
    bool Applied,
    string? BackupPath,
    int SchemaVersion,
    string? SafetyBackupPath,
    string? Error);

/// <summary>
/// 待恢复请求的执行器：只在启动早期（数据库尚未被业务打开）调用。
/// 先校验备份，再生成 pre-restore 快照后覆盖当前库；无论成功失败都会清理请求，
/// 避免坏请求在每次启动时反复失败。
/// </summary>
public sealed class PendingRestoreProcessor
{
    private readonly DatabaseBackupService _backups;
    private readonly PendingRestoreStore _store;

    public PendingRestoreProcessor(DatabaseBackupService backups, PendingRestoreStore store)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(store);

        _backups = backups;
        _store = store;
    }

    public PendingRestoreOutcome ApplyIfPending()
    {
        var request = _store.Read();
        if (request is null)
        {
            return new PendingRestoreOutcome(false, false, null, 0, null, null);
        }

        var backupPath = request.BackupPath;
        if (!File.Exists(backupPath))
        {
            _store.Clear();
            return new PendingRestoreOutcome(
                true,
                false,
                backupPath,
                0,
                null,
                "备份文件已不存在，恢复请求已取消。");
        }

        var validation = _backups.Validate(backupPath);
        if (!validation.IsValid)
        {
            _store.Clear();
            return new PendingRestoreOutcome(
                true,
                false,
                backupPath,
                0,
                null,
                $"备份文件不可用：{validation.Error}");
        }

        if (validation.SchemaVersion > MigrationCatalog.LatestVersion)
        {
            _store.Clear();
            return new PendingRestoreOutcome(
                true,
                false,
                backupPath,
                validation.SchemaVersion,
                null,
                $"备份来自更高版本（schema v{validation.SchemaVersion}），请先升级拾光再恢复。");
        }

        try
        {
            var safety = _backups.Restore(backupPath);
            _store.Clear();
            return new PendingRestoreOutcome(
                true,
                true,
                backupPath,
                validation.SchemaVersion,
                safety,
                null);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidDataException)
        {
            _store.Clear();
            return new PendingRestoreOutcome(
                true,
                false,
                backupPath,
                validation.SchemaVersion,
                null,
                $"恢复失败：{ex.Message}");
        }
    }
}
