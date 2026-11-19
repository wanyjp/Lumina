using Microsoft.Data.Sqlite;

namespace Lumina.Data.Sqlite;

/// <summary>
/// 仓储基类：统一通过 <see cref="SqliteDatabase"/> 的只读连接与写事务访问数据，
/// 避免各仓储自行打开连接或漏掉事务边界。P1 起的模块仓储从此派生。
/// </summary>
public abstract class RepositoryBase
{
    protected RepositoryBase(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        Database = database;
    }

    protected SqliteDatabase Database { get; }

    protected T Read<T>(Func<SqliteConnection, T> query) => Database.Read(query);

    protected void Write(Action<SqliteConnection, SqliteTransaction> action) =>
        Database.ExecuteInTransaction(action);

    protected T Write<T>(Func<SqliteConnection, SqliteTransaction, T> action) =>
        Database.ExecuteInTransaction(action);
}
