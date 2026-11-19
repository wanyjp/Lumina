using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Backup;

/// <summary>
/// 数据库损坏兜底（工程方案 2.4 / 风险 R7）：把无法打开的库连同 WAL/SHM 一起移入隔离目录，
/// 保留原文件供事后分析，再由调用方重建空库。绝不静默删除用户数据。
/// </summary>
public sealed class DatabaseRecoveryService
{
    private readonly SqliteDatabase _database;
    private readonly string _quarantineDirectory;

    public DatabaseRecoveryService(SqliteDatabase database, string quarantineDirectory)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(quarantineDirectory);

        _database = database;
        _quarantineDirectory = Path.GetFullPath(quarantineDirectory);
    }

    public string QuarantineDirectory => _quarantineDirectory;

    /// <summary>是否存在可隔离的库文件（存在且非空）。</summary>
    public bool HasRecoverableDatabase
    {
        get
        {
            var path = _database.DatabasePath;
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
    }

    /// <summary>把当前库与 sidecar 文件移入隔离目录，返回主库被移动到的路径。</summary>
    public string Quarantine()
    {
        if (!HasRecoverableDatabase)
        {
            throw new InvalidOperationException("当前没有可隔离的数据库文件。");
        }

        Directory.CreateDirectory(_quarantineDirectory);
        SqliteConnection.ClearAllPools();

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = MakeUnique(Path.Combine(_quarantineDirectory, $"lumina-corrupt-{stamp}.db"));
        File.Move(_database.DatabasePath, target);

        MoveSidecar(_database.DatabasePath + "-wal", target + "-wal");
        MoveSidecar(_database.DatabasePath + "-shm", target + "-shm");

        return target;
    }

    /// <summary>
    /// 判断异常是否属于“库文件损坏/不是数据库”，供启动流程决定是否提供重建选项。
    /// 版本过高、权限不足等不属于损坏，不能走重建。
    /// </summary>
    public static bool IsCorruption(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is SqliteException sqlite)
        {
            // SQLITE_CORRUPT = 11, SQLITE_NOTADB = 26
            if (sqlite.SqliteErrorCode is 11 or 26)
            {
                return true;
            }

            return ContainsCorruptionText(sqlite.Message);
        }

        return ContainsCorruptionText(exception.Message);
    }

    private static bool ContainsCorruptionText(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (message.Contains("malformed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not a database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("database corruption", StringComparison.OrdinalIgnoreCase)
            || message.Contains("file is encrypted", StringComparison.OrdinalIgnoreCase));

    private static void MoveSidecar(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        try
        {
            File.Move(source, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sidecar 移动失败不阻断主流程：主库已隔离，SQLite 重建时会生成新的 WAL。
        }
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}.db");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}.db");
    }
}
