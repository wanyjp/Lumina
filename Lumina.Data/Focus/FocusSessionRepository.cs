using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Focus;

/// <summary>
/// <c>focus_session</c> 表读写（P3-01）。`session_key` 唯一，重复写入被忽略。
/// 时间统一以 ISO 8601 带偏移保存，区间比较交给 SQLite 的 <c>datetime()</c> 归一到 UTC，
/// 因此用户切换时区后历史数据仍落在正确的 UTC 区间。
/// </summary>
public sealed class FocusSessionRepository : IFocusSessionStore
{
    private const string SelectColumns = """
        SELECT session_key, source, started_at, ended_at, duration_sec, finished, note
        FROM focus_session
        """;

    private readonly SqliteDatabase _database;

    public FocusSessionRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public int Add(FocusSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO focus_session
                    (session_key, source, started_at, ended_at, duration_sec, finished, note)
                VALUES
                    ($key, $source, $started, $ended, $duration, $finished, $note);
                """;
            command.Parameters.AddWithValue("$key", session.SessionKey);
            command.Parameters.AddWithValue("$source", SourceKey(session.Source));
            command.Parameters.AddWithValue("$started", Format(session.StartedAt));
            command.Parameters.AddWithValue("$ended", Format(session.EndedAt));
            command.Parameters.AddWithValue("$duration", session.DurationSeconds);
            command.Parameters.AddWithValue("$finished", session.Finished ? 1 : 0);
            command.Parameters.AddWithValue("$note", (object?)session.Note ?? DBNull.Value);

            return command.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<FocusSession> EndedBetween(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive) =>
        Query(
            // 时间戳统一为 UTC "O" 格式，字符串比较即时间比较，可走 ended_at 索引。
            $"{SelectColumns} WHERE ended_at >= $start AND ended_at < $end " +
            "ORDER BY ended_at ASC, id ASC;",
            command =>
            {
                command.Parameters.AddWithValue("$start", Format(startUtc));
                command.Parameters.AddWithValue("$end", Format(endUtcExclusive));
            });

    public IReadOnlyList<FocusSession> Recent(int limit) => Query(
        $"{SelectColumns} ORDER BY ended_at DESC, id DESC LIMIT $limit;",
        command => command.Parameters.AddWithValue("$limit", Math.Max(0, limit)));

    public int Count() => _database.Read(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM focus_session;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    });

    private IReadOnlyList<FocusSession> Query(string sql, Action<SqliteCommand>? configure) =>
        _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            var results = new List<FocusSession>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new FocusSession
                {
                    SessionKey = reader.GetString(0),
                    Source = ParseSource(reader.GetString(1)),
                    StartedAt = Parse(reader.GetString(2)),
                    EndedAt = Parse(reader.GetString(3)),
                    DurationSeconds = reader.GetInt32(4),
                    Finished = reader.GetInt32(5) != 0,
                    Note = reader.IsDBNull(6) ? null : reader.GetString(6),
                });
            }

            return results;
        });

    private static string Format(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);

    private static DateTimeOffset Parse(string value) =>
        SqliteConventions.ParseTimestamp(value);

    private static string SourceKey(FocusSource source) => source switch
    {
        FocusSource.Timer => "timer",
        _ => "pomodoro",
    };

    private static FocusSource ParseSource(string value) =>
        string.Equals(value, "timer", StringComparison.OrdinalIgnoreCase)
            ? FocusSource.Timer
            : FocusSource.Pomodoro;
}
