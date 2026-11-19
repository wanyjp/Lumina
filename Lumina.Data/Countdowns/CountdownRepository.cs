using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Countdowns;

/// <summary>
/// countdown_item 仓储：锚点列按重复方式分存，同一时刻只有一组有效锚点。
/// 枚举以稳定键落库（`work/life/exam/anniv/other`、`none/weekly/monthly/yearly`、
/// `none/ontime/m5/m15/m30/h1/d1`），读取时未知值回落默认。
/// </summary>
public sealed class CountdownRepository : ICountdownStore
{
    private const string SelectColumns = """
        id, legacy_id, name, category, repeat, remind,
        anchor_date, anchor_weekday, anchor_day, anchor_month, anchor_month_end,
        legacy_color, created_at, updated_at
        """;

    private readonly SqliteDatabase _database;

    public CountdownRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public IReadOnlyList<CountdownItem> QueryAll()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM countdown_item ORDER BY id;";

            using var reader = command.ExecuteReader();
            var items = new List<CountdownItem>();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        });
    }

    public CountdownItem? Find(long id)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM countdown_item WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        });
    }

    public long Insert(CountdownItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO countdown_item
                  (legacy_id, name, category, repeat, remind,
                   anchor_date, anchor_weekday, anchor_day, anchor_month, anchor_month_end,
                   legacy_color, created_at, updated_at)
                VALUES
                  ($legacyId, $name, $category, $repeat, $remind,
                   $anchorDate, $anchorWeekday, $anchorDay, $anchorMonth, $anchorMonthEnd,
                   $legacyColor, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            Bind(command, item);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(now));
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public void Update(CountdownItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE countdown_item SET
                  legacy_id = $legacyId,
                  name = $name,
                  category = $category,
                  repeat = $repeat,
                  remind = $remind,
                  anchor_date = $anchorDate,
                  anchor_weekday = $anchorWeekday,
                  anchor_day = $anchorDay,
                  anchor_month = $anchorMonth,
                  anchor_month_end = $anchorMonthEnd,
                  legacy_color = $legacyColor,
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
                command.CommandText = "DELETE FROM countdown_item WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                deleted += command.ExecuteNonQuery();
            }

            return deleted;
        });
    }

    private static void Bind(SqliteCommand command, CountdownItem item)
    {
        AddNullable(command, "$legacyId", item.LegacyId);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$category", CategoryKey(item.Category));
        command.Parameters.AddWithValue("$repeat", RepeatKey(item.Repeat));
        command.Parameters.AddWithValue("$remind", ReminderKey(item.Reminder));
        AddNullable(command, "$anchorDate", item.AnchorDate is { } date ? FormatDate(date) : null);
        AddNullable(command, "$anchorWeekday", item.AnchorWeekday);
        AddNullable(command, "$anchorDay", item.AnchorDay);
        AddNullable(command, "$anchorMonth", item.AnchorMonth);
        command.Parameters.AddWithValue("$anchorMonthEnd", item.AnchorMonthEnd ? 1 : 0);
        AddNullable(command, "$legacyColor", item.LegacyColor);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static CountdownItem Map(SqliteDataReader reader)
    {
        return new CountdownItem
        {
            Id = reader.GetInt64(0),
            LegacyId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Name = reader.GetString(2),
            Category = ParseCategory(reader.IsDBNull(3) ? null : reader.GetString(3)),
            Repeat = ParseRepeat(reader.IsDBNull(4) ? null : reader.GetString(4)),
            Reminder = ParseReminder(reader.IsDBNull(5) ? null : reader.GetString(5)),
            AnchorDate = reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6)),
            AnchorWeekday = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            AnchorDay = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            AnchorMonth = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            AnchorMonthEnd = !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
            LegacyColor = reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAt = ParseTimestamp(reader.GetString(12)),
            UpdatedAt = ParseTimestamp(reader.GetString(13)),
        };
    }

    private static string CategoryKey(CountdownCategory category) => category switch
    {
        CountdownCategory.Work => "work",
        CountdownCategory.Life => "life",
        CountdownCategory.Exam => "exam",
        CountdownCategory.Anniversary => "anniv",
        _ => "other",
    };

    private static string RepeatKey(CountdownRepeat repeat) => repeat switch
    {
        CountdownRepeat.Weekly => "weekly",
        CountdownRepeat.Monthly => "monthly",
        CountdownRepeat.Yearly => "yearly",
        _ => "none",
    };

    private static string ReminderKey(ReminderOffset offset) => offset switch
    {
        ReminderOffset.OnTime => "ontime",
        ReminderOffset.Minutes5 => "m5",
        ReminderOffset.Minutes15 => "m15",
        ReminderOffset.Minutes30 => "m30",
        ReminderOffset.Hour1 => "h1",
        ReminderOffset.Day1 => "d1",
        _ => "none",
    };

    private static CountdownCategory ParseCategory(string? value) => value switch
    {
        "work" => CountdownCategory.Work,
        "life" => CountdownCategory.Life,
        "exam" => CountdownCategory.Exam,
        "anniv" => CountdownCategory.Anniversary,
        _ => CountdownCategory.Other,
    };

    private static CountdownRepeat ParseRepeat(string? value) => value switch
    {
        "weekly" => CountdownRepeat.Weekly,
        "monthly" => CountdownRepeat.Monthly,
        "yearly" => CountdownRepeat.Yearly,
        _ => CountdownRepeat.None,
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

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);

    private static DateOnly ParseDate(string value) => SqliteConventions.ParseDate(value);

    private static DateTimeOffset ParseTimestamp(string value) =>
        SqliteConventions.ParseTimestamp(value);
}
