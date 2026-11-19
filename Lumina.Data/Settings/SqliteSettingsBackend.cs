using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Stores;

namespace Lumina.Data.Settings;

/// <summary>
/// 配置的 SQLite 后端：写入 <c>app_setting</c>，每次写入立即提交。
/// 值由上层序列化为文本，本类不解释值语义。
/// </summary>
public sealed class SqliteSettingsBackend : ISettingsBackend
{
    private readonly SqliteDatabase _database;

    public SqliteSettingsBackend(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_setting WHERE key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        });
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return _database.Read(connection =>
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM app_setting;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values[reader.GetString(0)] = reader.GetString(1);
            }

            return values;
        });
    }

    public void Set(string key, string value, string? module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var updatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO app_setting(key, value, module, updated_at)
                VALUES ($key, $value, $module, $updatedAt)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    module = excluded.module,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$module", module is null ? DBNull.Value : module);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
            command.ExecuteNonQuery();
        });
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
