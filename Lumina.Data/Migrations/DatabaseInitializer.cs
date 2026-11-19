using System.Globalization;
using Lumina.Data.Backup;
using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Migrations;

/// <summary>
/// 启动时的数据库初始化器：启用 WAL、建立 schema_version、按版本顺序执行迁移。
/// 待迁移且库中已有数据时，先写入 backups/ 再升级；单个迁移失败即回滚并保留旧库。
/// </summary>
public sealed class DatabaseInitializer
{
    private const string SchemaVersionTableSql = """
        CREATE TABLE IF NOT EXISTS schema_version (
          version     INTEGER PRIMARY KEY,
          applied_at  TEXT NOT NULL,
          description TEXT NOT NULL
        );
        """;

    private readonly SqliteDatabase _database;
    private readonly DatabaseBackupService _backups;
    private readonly IReadOnlyList<Migration> _migrations;

    public DatabaseInitializer(
        SqliteDatabase database,
        DatabaseBackupService backups,
        IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(migrations);
        ValidateMigrations(migrations);

        _database = database;
        _backups = backups;
        _migrations = migrations;
    }

    public DatabaseInitializationResult Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existedBefore = File.Exists(_database.DatabasePath)
            && new FileInfo(_database.DatabasePath).Length > 0;

        var journalMode = _database.EnsureWalMode();
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"无法启用 SQLite WAL 模式，当前 journal mode 为 {journalMode}。");
        }

        using var connection = _database.Open();
        EnsureSchemaVersionTable(connection);

        var previousVersion = ReadCurrentVersion(connection);
        var latestVersion = _migrations[^1].Version;
        if (previousVersion > latestVersion)
        {
            throw new InvalidOperationException(
                $"数据库版本 v{previousVersion} 高于应用支持的 v{latestVersion}，请升级应用后再打开。");
        }

        var pending = _migrations.Where(migration => migration.Version > previousVersion).ToList();
        string? backupPath = null;
        if (pending.Count > 0 && (previousVersion > 0 || existedBefore))
        {
            backupPath = _backups.CreateBackup("pre-migration", previousVersion);
        }

        var currentVersion = previousVersion;
        foreach (var migration in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyMigration(connection, migration, currentVersion, backupPath);
            currentVersion = migration.Version;
        }

        return new DatabaseInitializationResult(
            previousVersion,
            currentVersion,
            pending.Count,
            backupPath,
            ProbeFts5(connection));
    }

    private static void ApplyMigration(
        SqliteConnection connection,
        Migration migration,
        int previousVersion,
        string? backupPath)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            ExecuteScript(connection, transaction, migration.Sql);

            using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = """
                    INSERT INTO schema_version(version, applied_at, description)
                    VALUES ($version, $appliedAt, $description);
                    """;
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                record.Parameters.AddWithValue("$description", migration.Description);
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            try
            {
                transaction.Rollback();
            }
            catch (InvalidOperationException)
            {
                // 事务已被 SQLite 自动回滚时忽略。
            }

            throw new MigrationFailedException(
                migration.Version,
                migration.Description,
                previousVersion,
                backupPath,
                ex);
        }
    }

    /// <summary>
    /// 逐语句执行迁移脚本：schema_version 丢失后重跑时，
    /// 已存在的列（ALTER TABLE ... ADD COLUMN）跳过而不是中断整个迁移。
    /// </summary>
    private static void ExecuteScript(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        foreach (var statement in SplitStatements(sql))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;

            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (IsAlreadyApplied(statement, ex))
            {
                // 该列已存在：视为本语句已应用。
            }
        }
    }

    private static bool IsAlreadyApplied(string statement, SqliteException exception) =>
        statement.TrimStart().StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
        && exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);

    /// <summary>按行尾分号拆分语句；迁移目录内的脚本不含字符串内分号。</summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var line in sql.Split('\n'))
        {
            if (line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            builder.AppendLine(line);
            if (!line.TrimEnd().EndsWith(';'))
            {
                continue;
            }

            var statement = builder.ToString().Trim();
            builder.Clear();
            if (statement.Length > 0)
            {
                yield return statement;
            }
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private static void EnsureSchemaVersionTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SchemaVersionTableSql;
        command.ExecuteNonQuery();
    }

    private static int ReadCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool ProbeFts5(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5');";
        using var reader = command.ExecuteReader();
        return reader.Read()
            && Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture) == 1L;
    }

    private static void ValidateMigrations(IReadOnlyList<Migration> migrations)
    {
        if (migrations.Count == 0)
        {
            throw new ArgumentException("至少需要一个迁移。", nameof(migrations));
        }

        var lastVersion = 0;
        foreach (var migration in migrations)
        {
            if (migration.Version <= lastVersion)
            {
                throw new ArgumentException(
                    $"迁移版本必须严格递增：v{migration.Version} 出现在 v{lastVersion} 之后。",
                    nameof(migrations));
            }

            if (string.IsNullOrWhiteSpace(migration.Description) || string.IsNullOrWhiteSpace(migration.Sql))
            {
                throw new ArgumentException($"迁移 v{migration.Version} 缺少描述或 SQL。", nameof(migrations));
            }

            lastVersion = migration.Version;
        }
    }
}
