using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Notes;

/// <summary>
/// note_item 仓储：标题/正文/颜色/位置尺寸/置顶与时间戳；
/// 排序在 SQL 层完成（置顶优先、更新时间倒序），界面不再二次排序。
/// </summary>
public sealed class NoteRepository : INoteStore
{
    private const string SelectColumns = """
        id, legacy_id, title, content, color,
        pos_x, pos_y, width, height, pinned, created_at, updated_at
        """;

    private readonly SqliteDatabase _database;

    public NoteRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public IReadOnlyList<NoteItem> QueryAll()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SelectColumns} FROM note_item
                ORDER BY pinned DESC, updated_at DESC, id DESC;
                """;

            using var reader = command.ExecuteReader();
            var items = new List<NoteItem>();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        });
    }

    public NoteItem? Find(long id)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM note_item WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        });
    }

    public long Insert(NoteItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO note_item
                  (legacy_id, title, content, color,
                   pos_x, pos_y, width, height, pinned, created_at, updated_at)
                VALUES
                  ($legacyId, $title, $content, $color,
                   $posX, $posY, $width, $height, $pinned, $createdAt, $updatedAt);
                SELECT last_insert_rowid();
                """;
            Bind(command, item);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(now));
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public void Update(NoteItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE note_item SET
                  legacy_id = $legacyId,
                  title = $title,
                  content = $content,
                  color = $color,
                  pos_x = $posX,
                  pos_y = $posY,
                  width = $width,
                  height = $height,
                  pinned = $pinned,
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
                command.CommandText = "DELETE FROM note_item WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                deleted += command.ExecuteNonQuery();
            }

            return deleted;
        });
    }

    private static void Bind(SqliteCommand command, NoteItem item)
    {
        AddNullable(command, "$legacyId", item.LegacyId);
        AddNullable(command, "$title", item.Title);
        command.Parameters.AddWithValue("$content", item.Content);
        AddNullable(command, "$color", item.Color);
        AddNullable(command, "$posX", item.X);
        AddNullable(command, "$posY", item.Y);
        AddNullable(command, "$width", item.Width);
        AddNullable(command, "$height", item.Height);
        command.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static NoteItem Map(SqliteDataReader reader)
    {
        return new NoteItem
        {
            Id = reader.GetInt64(0),
            LegacyId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Title = reader.IsDBNull(2) ? null : reader.GetString(2),
            Content = reader.GetString(3),
            Color = reader.IsDBNull(4) ? null : reader.GetString(4),
            X = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            Y = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            Width = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            Height = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            Pinned = !reader.IsDBNull(9) && reader.GetInt32(9) != 0,
            CreatedAt = ParseTimestamp(reader.GetString(10)),
            UpdatedAt = ParseTimestamp(reader.GetString(11)),
        };
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);

    private static DateTimeOffset ParseTimestamp(string value) =>
        SqliteConventions.ParseTimestamp(value);
}
