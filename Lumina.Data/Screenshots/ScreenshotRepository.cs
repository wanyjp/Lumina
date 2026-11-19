using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Screenshots;

/// <summary>
/// <c>screenshot_meta</c> 表读写。路径以相对 <c>files/</c> 的字符串保存，
/// 与 <c>OrphanFileReconciler</c> 的口径一致（正斜杠、无前导分隔符）。
/// </summary>
public sealed class ScreenshotRepository : IScreenshotCatalog
{
    private const string SelectColumns = """
        SELECT id, capture_type, file_path, thumb_path, ocr_text, width, height, created_at
        FROM screenshot_meta
        """;

    private readonly SqliteDatabase _database;

    public ScreenshotRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public long Add(ScreenshotRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO screenshot_meta
                    (capture_type, file_path, thumb_path, ocr_text, width, height, created_at)
                VALUES
                    ($type, $path, $thumb, $ocr, $width, $height, $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$type", record.CaptureType);
            command.Parameters.AddWithValue("$path", record.FilePath);
            command.Parameters.AddWithValue("$thumb", (object?)record.ThumbPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$ocr", (object?)record.OcrText ?? DBNull.Value);
            command.Parameters.AddWithValue("$width", record.Width);
            command.Parameters.AddWithValue("$height", record.Height);
            command.Parameters.AddWithValue("$created", SqliteConventions.FormatTimestamp(record.CreatedAt));

            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public IReadOnlyList<ScreenshotRecord> Recent(int limit) => Query(
        $"{SelectColumns} ORDER BY created_at DESC, id DESC LIMIT $limit;",
        command => command.Parameters.AddWithValue("$limit", limit));

    public IReadOnlyList<ScreenshotRecord> All() => Query(
        $"{SelectColumns} ORDER BY created_at ASC, id ASC;",
        configure: null);

    public IReadOnlyList<ScreenshotRecord> Oldest(int count) => Query(
        $"{SelectColumns} ORDER BY created_at ASC, id ASC LIMIT $count;",
        command => command.Parameters.AddWithValue("$count", count));

    public ScreenshotRecord? Find(long id) => Query(
        $"{SelectColumns} WHERE id = $id;",
        command => command.Parameters.AddWithValue("$id", id)).FirstOrDefault();

    public int Count() => _database.Read(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM screenshot_meta;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    });

    public void Delete(long id) => _database.ExecuteInTransaction((connection, transaction) =>
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM screenshot_meta WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    });

    private IReadOnlyList<ScreenshotRecord> Query(string sql, Action<SqliteCommand>? configure)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            using var reader = command.ExecuteReader();
            var records = new List<ScreenshotRecord>();
            while (reader.Read())
            {
                records.Add(new ScreenshotRecord(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    SqliteConventions.ParseTimestamp(reader.GetString(7))));
            }

            return records;
        });
    }
}
