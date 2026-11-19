using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Todos;

/// <summary>
/// todo_item 仓储：全量/区间查询与 CRUD。完成态与完成时间独立列，
/// 编辑时由服务层保留，避免覆盖丢失。
/// </summary>
public sealed class TodoRepository : ITodoStore
{
    private const string SelectColumns = """
        id, legacy_id, text, due_date, due_time, priority, done, done_at, note, note_id, created_at, updated_at
        """;

    private readonly SqliteDatabase _database;

    public TodoRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public IReadOnlyList<TodoItem> QueryAll()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM todo_item ORDER BY created_at DESC, id DESC;";
            using var reader = command.ExecuteReader();
            var items = new List<TodoItem>();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        });
    }

    public IReadOnlyList<TodoItem> QueryRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return [];
        }

        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM todo_item
                WHERE due_date IS NOT NULL AND due_date >= $start AND due_date <= $end
                ORDER BY due_date, COALESCE(due_time, '00:00'), id;
                """;
            command.Parameters.AddWithValue("$start", FormatDate(start));
            command.Parameters.AddWithValue("$end", FormatDate(end));

            using var reader = command.ExecuteReader();
            var items = new List<TodoItem>();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        });
    }

    public TodoItem? Find(long id)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM todo_item WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        });
    }

    public long Insert(TodoItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO todo_item
                  (legacy_id, text, due_date, due_time, priority, done, done_at, note, note_id, created_at, updated_at)
                VALUES
                  ($legacyId, $text, $dueDate, $dueTime, $priority, $done, $doneAt, $note, $noteId, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            Bind(command, item);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(now));
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public void Update(TodoItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE todo_item SET
                  legacy_id = $legacyId,
                  text = $text,
                  due_date = $dueDate,
                  due_time = $dueTime,
                  priority = $priority,
                  done = $done,
                  done_at = $doneAt,
                  note = $note,
                  note_id = $noteId,
                  updated_at = $updatedAt
                WHERE id = $id;
                """;
            Bind(command, item);
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            command.Parameters.AddWithValue("$id", item.Id);
            command.ExecuteNonQuery();
        });
    }

    public int Delete(IEnumerable<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var targets = ids.Where(id => id > 0).Distinct().ToArray();
        if (targets.Length == 0)
        {
            return 0;
        }

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            var deleted = 0;
            foreach (var id in targets)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM todo_item WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                deleted += command.ExecuteNonQuery();
            }

            return deleted;
        });
    }

    private static void Bind(SqliteCommand command, TodoItem item)
    {
        AddNullable(command, "$legacyId", item.LegacyId);
        command.Parameters.AddWithValue("$text", item.Text);
        AddNullable(command, "$dueDate", item.DueDate is { } date ? FormatDate(date) : null);
        AddNullable(command, "$dueTime", item.DueTime is { } time ? FormatTime(time) : null);
        command.Parameters.AddWithValue("$priority", PriorityKey(item.Priority));
        command.Parameters.AddWithValue("$done", item.Done ? 1 : 0);
        AddNullable(command, "$doneAt", item.DoneAt is { } doneAt ? doneAt.ToString("O", CultureInfo.InvariantCulture) : null);
        command.Parameters.AddWithValue("$note", item.Note);
        AddNullable(command, "$noteId", item.NoteId);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static TodoItem Map(SqliteDataReader reader)
    {
        return new TodoItem
        {
            Id = reader.GetInt64(0),
            LegacyId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Text = reader.GetString(2),
            DueDate = reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)),
            DueTime = reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
            Priority = ParsePriority(reader.IsDBNull(5) ? null : reader.GetString(5)),
            Done = reader.IsDBNull(6) ? false : reader.GetInt32(6) != 0,
            DoneAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            Note = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            NoteId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
        };
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);

    private static DateOnly ParseDate(string value) => SqliteConventions.ParseDate(value);

    private static TimeOnly ParseTime(string value)
    {
        foreach (var format in new[] { "HH:mm", "HH:mm:ss" })
        {
            if (TimeOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return TimeOnly.MinValue;
    }

    private static string PriorityKey(Priority priority) => priority switch
    {
        Priority.High => "high",
        Priority.Low => "low",
        _ => "normal",
    };

    private static Priority ParsePriority(string? value) => value switch
    {
        "high" => Priority.High,
        "low" => Priority.Low,
        _ => Priority.Normal,
    };
}
