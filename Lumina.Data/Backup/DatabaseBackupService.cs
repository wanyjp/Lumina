using System.Globalization;
using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Backup;

/// <summary>备份文件校验结果。</summary>
public sealed record BackupValidationResult(bool IsValid, int SchemaVersion, string? Error);

/// <summary>
/// 数据库备份与恢复骨架：使用 SQLite 在线备份 API 生成一致快照，
/// 备份文件统一转为非 WAL 的单文件，便于校验、导出与恢复。
/// 快照只包含数据库；截图、剪贴板图片与便签附件仍存放在 files 目录，不随快照打包。
/// </summary>
public sealed class DatabaseBackupService
{
    private readonly SqliteDatabase _database;
    private readonly string _backupDirectory;

    public DatabaseBackupService(SqliteDatabase database, string backupDirectory)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        _database = database;
        _backupDirectory = Path.GetFullPath(backupDirectory);
    }

    public string BackupDirectory => _backupDirectory;

    /// <summary>
    /// 在备份目录生成快照，文件名含时间、源版本与原因。
    /// <paramref name="reason"/> 只保留字母、数字与连字符。
    /// </summary>
    public string CreateBackup(string reason, int schemaVersion)
    {
        Directory.CreateDirectory(_backupDirectory);

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var safeReason = SanitizeReason(reason);
        var fileName = $"lumina-{stamp}-v{schemaVersion}-{safeReason}.db";
        var path = MakeUnique(Path.Combine(_backupDirectory, fileName));

        BackupTo(path);
        return path;
    }

    /// <summary>把当前库的在线快照写入指定路径；失败时清理半成品。</summary>
    public void BackupTo(string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath)) ?? ".");

        try
        {
            using var source = _database.Open();
            using var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = destinationPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    // 不池化：连接释放后立即释放文件句柄，备份文件可被校验/复制/归档。
                    Pooling = false,
                }.ToString());
            destination.Open();

            source.BackupDatabase(destination);

            // 备份文件保持为单一文件，避免恢复后再依赖 -wal / -shm。
            using var normalize = destination.CreateCommand();
            normalize.CommandText = "PRAGMA journal_mode=DELETE;";
            normalize.ExecuteNonQuery();
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    /// <summary>校验备份是否完整且可读；只读打开，不修改备份文件。</summary>
    public BackupValidationResult Validate(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return new BackupValidationResult(false, 0, "备份文件不存在");
        }

        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString());
            connection.Open();

            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var integrityResult = integrity.ExecuteScalar() as string;
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new BackupValidationResult(false, 0, $"完整性校验失败：{integrityResult}");
            }

            using var version = connection.CreateCommand();
            version.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            var value = version.ExecuteScalar();
            return new BackupValidationResult(
                true,
                Convert.ToInt32(value, CultureInfo.InvariantCulture),
                null);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return new BackupValidationResult(false, 0, ex.Message);
        }
    }

    /// <summary>
    /// 用备份覆盖当前库，覆盖前自动生成 pre-restore 快照。
    /// 只能在启动早期、没有活跃读写连接时调用；返回覆盖前的快照路径（当前库不存在时为 null）。
    /// </summary>
    public string? Restore(string backupPath)
    {
        var validation = Validate(backupPath);
        if (!validation.IsValid)
        {
            throw new InvalidDataException($"备份文件不可用：{validation.Error}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_database.DatabasePath) ?? ".");

        // 先把备份复制并校验到临时文件：复制/校验失败时当前库完全不受影响。
        var tempPath = _database.DatabasePath + ".restore.tmp";
        try
        {
            File.Copy(backupPath, tempPath, overwrite: true);
            var staged = Validate(tempPath);
            if (!staged.IsValid)
            {
                throw new InvalidDataException($"备份复制后校验失败：{staged.Error}");
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        string? safetyBackup = null;
        string? renamedOriginal = null;
        try
        {
            if (File.Exists(_database.DatabasePath))
            {
                try
                {
                    safetyBackup = CreateBackup("pre-restore", ReadCurrentSchemaVersion());
                }
                catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
                {
                    // 当前库已损坏、无法读出 schema：跳过安全快照，改为把原文件改名保留，
                    // 保证“覆盖前一定有一份旧数据”，同时允许用备份救回可用状态。
                }
            }

            SqliteConnection.ClearAllPools();
            DeleteSidecarFiles(_database.DatabasePath);

            if (safetyBackup is null && File.Exists(_database.DatabasePath))
            {
                var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                renamedOriginal = _database.DatabasePath + $".pre-restore-{stamp}";
                File.Move(_database.DatabasePath, renamedOriginal);
            }

            File.Move(tempPath, _database.DatabasePath, overwrite: true);
            renamedOriginal = null;
            return safetyBackup;
        }
        catch
        {
            // 覆盖失败时把改名保留的原库改回原位，避免下次启动把“数据还在”误判成空库。
            if (renamedOriginal is not null && File.Exists(renamedOriginal)
                && !File.Exists(_database.DatabasePath))
            {
                try
                {
                    File.Move(renamedOriginal, _database.DatabasePath);
                }
                catch (Exception)
                {
                    // 回滚也失败时保留 .pre-restore 文件，数据未丢失，交由人工处理。
                }
            }

            throw;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private int ReadCurrentSchemaVersion()
    {
        if (!File.Exists(_database.DatabasePath))
        {
            return 0;
        }

        try
        {
            return _database.Read(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            });
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
    }

    private static string SanitizeReason(string reason)
    {
        var chars = reason
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
            .ToArray();
        var cleaned = new string(chars).Trim('-');
        return cleaned.Length == 0 ? "manual" : cleaned;
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不掩盖主流程异常。
        }
    }
}
