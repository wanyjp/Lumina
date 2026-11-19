using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Reminders;

/// <summary>reminder_dedup 仓储：命中查询、写入与按时间淘汰。</summary>
public sealed class ReminderDedupRepository : IReminderDedupStore
{
    private readonly SqliteDatabase _database;

    public ReminderDedupRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public bool Contains(string triggerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerKey);

        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM reminder_dedup WHERE trigger_key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", triggerKey);
            return command.ExecuteScalar() is not null;
        });
    }

    public void Mark(string triggerKey, DateTimeOffset firedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerKey);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO reminder_dedup (trigger_key, fired_at)
                VALUES ($key, $firedAt)
                ON CONFLICT(trigger_key) DO UPDATE SET fired_at = excluded.fired_at;
                """;
            command.Parameters.AddWithValue("$key", triggerKey);
            command.Parameters.AddWithValue("$firedAt", FormatTimestamp(firedAt));
            command.ExecuteNonQuery();
        });
    }

    public int Count()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM reminder_dedup;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public int TrimTo(int target)
    {
        if (target < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM reminder_dedup
                WHERE trigger_key NOT IN (
                  SELECT trigger_key
                  FROM reminder_dedup
                  ORDER BY fired_at DESC, trigger_key DESC
                  LIMIT $target
                );
                """;
            command.Parameters.AddWithValue("$target", target);
            return command.ExecuteNonQuery();
        });
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);
}
