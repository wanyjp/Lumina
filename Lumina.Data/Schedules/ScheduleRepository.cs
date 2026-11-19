using System.Globalization;
using System.Text.Json;
using Lumina.Data.Sqlite;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Schedules;

/// <summary>
/// schedule_event 仓储：CRUD 与按区间交集查询。
/// 日期存为 <c>yyyy-MM-dd</c>，时间存为 <c>HH:mm</c>；空时间保留为 NULL，
/// 以区分“全天”和“开放结束”。周期规则以 JSON 原样存取，展开由 P1-07 负责。
/// </summary>
public sealed class ScheduleRepository : IScheduleStore
{
    private const string SelectColumns = """
        id, legacy_id, title, start_date, start_time, end_date, end_time,
        location, note, priority, repeat_rule, remind, done
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteDatabase _database;

    public ScheduleRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public IReadOnlyList<ScheduleEvent> QueryRange(DateOnly start, DateOnly end)
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
                FROM schedule_event
                WHERE start_date <= $end AND COALESCE(end_date, start_date) >= $start
                ORDER BY start_date, COALESCE(start_time, '00:00'), id;
                """;
            command.Parameters.AddWithValue("$start", FormatDate(start));
            command.Parameters.AddWithValue("$end", FormatDate(end));

            using var reader = command.ExecuteReader();
            var events = new List<ScheduleEvent>();
            while (reader.Read())
            {
                events.Add(Map(reader));
            }

            return events;
        });
    }

    public IReadOnlyList<ScheduleEvent> QueryRecurring(DateOnly end)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM schedule_event
                WHERE repeat_rule IS NOT NULL AND start_date <= $end
                ORDER BY start_date, id;
                """;
            command.Parameters.AddWithValue("$end", FormatDate(end));

            using var reader = command.ExecuteReader();
            var events = new List<ScheduleEvent>();
            while (reader.Read())
            {
                events.Add(Map(reader));
            }

            return events;
        });
    }

    public ScheduleEvent? Find(long id)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM schedule_event WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        });
    }

    public long Insert(ScheduleEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schedule_event
                  (legacy_id, title, start_date, start_time, end_date, end_time,
                   location, note, priority, repeat_rule, remind, done, created_at, updated_at)
                VALUES
                  ($legacyId, $title, $startDate, $startTime, $endDate, $endTime,
                   $location, $note, $priority, $repeatRule, $remind, $done, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            Bind(command, item);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(now));
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));

            var id = command.ExecuteScalar();
            return Convert.ToInt64(id, CultureInfo.InvariantCulture);
        });
    }

    public void Update(ScheduleEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE schedule_event SET
                  legacy_id = $legacyId,
                  title = $title,
                  start_date = $startDate,
                  start_time = $startTime,
                  end_date = $endDate,
                  end_time = $endTime,
                  location = $location,
                  note = $note,
                  priority = $priority,
                  repeat_rule = $repeatRule,
                  remind = $remind,
                  done = $done,
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
                command.CommandText = "DELETE FROM schedule_event WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                deleted += command.ExecuteNonQuery();
            }

            return deleted;
        });
    }

    private static void Bind(SqliteCommand command, ScheduleEvent item)
    {
        AddNullable(command, "$legacyId", item.LegacyId);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$startDate", FormatDate(item.StartDate));
        AddNullable(command, "$startTime", item.StartTime is { } startTime ? FormatTime(startTime) : null);
        AddNullable(command, "$endDate", item.EndDate is { } endDate ? FormatDate(endDate) : null);
        AddNullable(command, "$endTime", item.EndTime is { } endTime ? FormatTime(endTime) : null);
        command.Parameters.AddWithValue("$location", item.Location);
        AddNullable(command, "$note", item.Note);
        command.Parameters.AddWithValue("$priority", PriorityKey(item.Priority));
        AddNullable(
            command,
            "$repeatRule",
            item.Repeat is null ? null : JsonSerializer.Serialize(item.Repeat, JsonOptions));
        command.Parameters.AddWithValue("$remind", item.Reminder.ToKey());
        command.Parameters.AddWithValue("$done", item.Done ? 1 : 0);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static ScheduleEvent Map(SqliteDataReader reader)
    {
        return new ScheduleEvent
        {
            Id = reader.GetInt64(0),
            LegacyId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Title = reader.GetString(2),
            StartDate = ParseDate(reader.GetString(3)),
            StartTime = reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
            EndDate = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
            EndTime = reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)),
            Location = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            Note = reader.IsDBNull(8) ? null : reader.GetString(8),
            Priority = ParsePriority(reader.IsDBNull(9) ? null : reader.GetString(9)),
            Repeat = reader.IsDBNull(10) ? null : ParseRepeat(reader.GetString(10)),
            Reminder = ParseReminder(reader.IsDBNull(11) ? null : reader.GetString(11)),
            Done = reader.IsDBNull(12) ? false : reader.GetInt32(12) != 0,
        };
    }

    internal static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    internal static string FormatTimestamp(DateTimeOffset value) =>
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

    private static ReminderOffset ParseReminder(string? value) => value switch
    {
        "ontime" => ReminderOffset.OnTime,
        "m5" => ReminderOffset.Minutes5,
        "m15" => ReminderOffset.Minutes15,
        "m30" => ReminderOffset.Minutes30,
        "h1" => ReminderOffset.Hour1,
        "d1" => ReminderOffset.Day1,
        _ => ReminderOffset.None,
    };

    private static RepeatRule? ParseRepeat(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<RepeatRule>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
