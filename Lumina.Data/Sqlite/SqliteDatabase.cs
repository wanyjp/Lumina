using Microsoft.Data.Sqlite;

namespace Lumina.Data.Sqlite;

/// <summary>
/// SQLite 连接入口：写连接按需打开，读连接强制只读（<c>query_only=1</c>）。
/// WAL、busy_timeout 与事务边界统一在此收敛，业务仓储不直接拼连接串。
/// </summary>
public sealed class SqliteDatabase
{
    public const int DefaultBusyTimeoutMilliseconds = 5000;

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    /// <summary>切换到 WAL 并返回实际 journal mode；迁移开始前调用一次。</summary>
    public string EnsureWalMode()
    {
        Directory.CreateDirectory(DirectoryOf(DatabasePath));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        return (command.ExecuteScalar() as string ?? string.Empty).ToLowerInvariant();
    }

    /// <summary>打开可写连接；数据库不存在时自动创建。</summary>
    public SqliteConnection Open() => OpenCore(SqliteOpenMode.ReadWriteCreate);

    /// <summary>打开只读连接；写入会被 SQLite 拒绝。</summary>
    public SqliteConnection OpenReadOnly() => OpenCore(SqliteOpenMode.ReadOnly);

    /// <summary>在只读连接上执行查询。</summary>
    public T Read<T>(Func<SqliteConnection, T> query)
    {
        using var connection = OpenReadOnly();
        return query(connection);
    }

    /// <summary>在单个写事务内执行操作；异常时回滚并原样抛出。</summary>
    public void ExecuteInTransaction(Action<SqliteConnection, SqliteTransaction> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExecuteInTransaction((connection, transaction) =>
        {
            action(connection, transaction);
            return true;
        });
    }

    /// <summary>在单个写事务内执行操作并返回结果；异常时回滚并原样抛出。</summary>
    public T ExecuteInTransaction<T>(Func<SqliteConnection, SqliteTransaction, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var result = action(connection, transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            TryRollback(transaction);
            throw;
        }
    }

    private SqliteConnection OpenCore(SqliteOpenMode mode)
    {
        if (mode != SqliteOpenMode.ReadOnly)
        {
            Directory.CreateDirectory(DirectoryOf(DatabasePath));
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = mode,
            ForeignKeys = true,
            Pooling = true,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = mode == SqliteOpenMode.ReadOnly
            ? $"PRAGMA busy_timeout={DefaultBusyTimeoutMilliseconds}; PRAGMA query_only=1;"
            : $"PRAGMA busy_timeout={DefaultBusyTimeoutMilliseconds}; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static void TryRollback(SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (InvalidOperationException)
        {
            // 连接已关闭或事务已结束时无需再次回滚。
        }
    }

    private static string DirectoryOf(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }
}
