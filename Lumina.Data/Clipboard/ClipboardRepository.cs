using System.Globalization;
using System.Text.Json;
using Lumina.Data.Sqlite;
using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Clipboard;

/// <summary>
/// clipboard_item 仓储：写入时在单个事务内完成时间窗去重合并与保留上限淘汰；
/// 命中重复则累加 dup_count、刷新 updated_at 并前移，否则插入新记录。
/// </summary>
public sealed class ClipboardRepository : RepositoryBase, IClipboardStore
{
    /// <summary>单条记录最多保留的来源数量，避免长期合并无限增长。</summary>
    private const int MaxSources = 8;

    private const string SelectColumns = """
        id, legacy_id, kind, text_content, file_path, thumb_path, url, mime,
        width, height, content_hash, normalized_text, pinned, pinned_at, note,
        tags, source_window, dup_count, created_at, updated_at, deleted_at, sources
        """;

    public ClipboardRepository(SqliteDatabase database)
        : base(database)
    {
    }

    public ClipboardSaveResult Save(
        ClipboardItem item,
        DateTimeOffset now,
        TimeSpan duplicateWindow,
        int retentionLimit,
        bool mergeDifferentSources = true)
    {
        ArgumentNullException.ThrowIfNull(item);

        var since = FormatTimestamp(now - duplicateWindow);
        var updatedAt = FormatTimestamp(now);
        var limit = EvictionPolicy.ClampLimit(retentionLimit);

        return Write((connection, transaction) =>
        {
            ClipboardItem saved;
            var merged = false;

            var duplicate = string.IsNullOrEmpty(item.ContentHash)
                ? null
                : FindDuplicate(
                    connection,
                    transaction,
                    item.ContentHash,
                    since,
                    item.SourceWindow,
                    matchSource: !mergeDifferentSources);

            if (duplicate is not null)
            {
                var sources = MergeSources(duplicate, item.SourceWindow);
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE clipboard_item SET
                      dup_count = dup_count + 1,
                      updated_at = $updatedAt,
                      source_window = COALESCE($sourceWindow, source_window),
                      sources = $sources
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$updatedAt", updatedAt);
                AddNullable(update, "$sourceWindow", item.SourceWindow);
                AddNullable(update, "$sources", FormatSources(sources));
                update.Parameters.AddWithValue("$id", duplicate.Id);
                update.ExecuteNonQuery();

                saved = FindById(connection, transaction, duplicate.Id)
                    ?? throw new InvalidOperationException("剪贴板合并后未找到记录。");
                merged = true;
            }
            else
            {
                var id = Insert(connection, transaction, item, updatedAt);
                saved = FindById(connection, transaction, id)
                    ?? throw new InvalidOperationException("剪贴板写入后未找到记录。");
            }

            var evicted = Evict(connection, transaction, limit);
            return new ClipboardSaveResult(saved, merged, evicted);
        });
    }

    public IReadOnlyList<ClipboardItem> QueryRecent(int limit)
    {
        var take = Math.Clamp(limit, 1, EvictionPolicy.MaxLimit);
        return Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {SelectColumns}
                FROM clipboard_item
                WHERE deleted_at IS NULL
                ORDER BY pinned DESC, pinned_at DESC, updated_at DESC, id DESC
                LIMIT $take;
                """;
            command.Parameters.AddWithValue("$take", take);

            using var reader = command.ExecuteReader();
            var items = new List<ClipboardItem>();
            while (reader.Read())
            {
                items.Add(Map(reader));
            }

            return items;
        });
    }

    public IReadOnlyList<ClipboardItem> EvictOldestNonPinned(int retentionLimit)
    {
        var limit = EvictionPolicy.ClampLimit(retentionLimit);
        return Write((connection, transaction) => Evict(connection, transaction, limit));
    }

    public bool SetPinned(long id, bool pinned, DateTimeOffset now)
    {
        return Write((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clipboard_item
                SET pinned = $pinned, pinned_at = $pinnedAt
                WHERE id = $id AND deleted_at IS NULL;
                """;
            command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
            AddNullable(
                command,
                "$pinnedAt",
                pinned ? FormatTimestamp(now) : null);
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        });
    }

    public bool SoftDelete(long id, DateTimeOffset now)
    {
        var deletedAt = FormatTimestamp(now);
        return Write((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clipboard_item SET deleted_at = $deletedAt
                WHERE id = $id AND deleted_at IS NULL;
                """;
            command.Parameters.AddWithValue("$deletedAt", deletedAt);
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        });
    }

    public ClipboardItem? HardDelete(long id)
    {
        return Write((connection, transaction) =>
        {
            var item = FindById(connection, transaction, id);
            if (item is null)
            {
                return null;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM clipboard_item WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
            return item;
        });
    }

    public IReadOnlyList<ClipboardItem> PurgeSoftDeletedBefore(DateTimeOffset cutoff)
    {
        var cutoffText = FormatTimestamp(cutoff);
        return Write((connection, transaction) =>
        {
            var items = new List<ClipboardItem>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = $"""
                    SELECT {SelectColumns}
                    FROM clipboard_item
                    WHERE deleted_at IS NOT NULL AND deleted_at < $cutoff
                    ORDER BY deleted_at ASC, id ASC;
                    """;
                select.Parameters.AddWithValue("$cutoff", cutoffText);

                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(Map(reader));
                }
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM clipboard_item
                WHERE deleted_at IS NOT NULL AND deleted_at < $cutoff;
                """;
            delete.Parameters.AddWithValue("$cutoff", cutoffText);
            delete.ExecuteNonQuery();
            return (IReadOnlyList<ClipboardItem>)items;
        });
    }

    public ClipboardItem? Touch(long id, DateTimeOffset now)
    {
        var updatedAt = FormatTimestamp(now);
        return Write((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clipboard_item SET updated_at = $updatedAt
                WHERE id = $id AND deleted_at IS NULL;
                """;
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
            command.Parameters.AddWithValue("$id", id);

            return command.ExecuteNonQuery() > 0
                ? FindById(connection, transaction, id)
                : null;
        });
    }

    public ClipboardItem? UpdateAnnotation(long id, string? note, IReadOnlyList<string> tags)
    {
        var tagsJson = JsonSerializer.Serialize(tags ?? Array.Empty<string>());
        return Write((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clipboard_item SET note = $note, tags = $tags
                WHERE id = $id AND deleted_at IS NULL;
                """;
            AddNullable(command, "$note", string.IsNullOrWhiteSpace(note) ? null : note.Trim());
            command.Parameters.AddWithValue("$tags", tagsJson);
            command.Parameters.AddWithValue("$id", id);

            return command.ExecuteNonQuery() > 0
                ? FindById(connection, transaction, id)
                : null;
        });
    }

    public ClipboardItem? UpdateContent(long id, ClipboardItem content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        var updatedAt = FormatTimestamp(now);

        return Write((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clipboard_item SET
                  kind = $kind,
                  text_content = $textContent,
                  url = $url,
                  mime = $mime,
                  normalized_text = $normalizedText,
                  content_hash = $contentHash,
                  updated_at = $updatedAt
                WHERE id = $id AND deleted_at IS NULL;
                """;
            command.Parameters.AddWithValue("$kind", ClipClassifierKeys.KindKey(content.Kind));
            AddNullable(command, "$textContent", content.TextContent);
            AddNullable(command, "$url", content.Url);
            AddNullable(command, "$mime", content.Mime);
            AddNullable(command, "$normalizedText", content.NormalizedText);
            AddNullable(command, "$contentHash", content.ContentHash);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
            command.Parameters.AddWithValue("$id", id);

            return command.ExecuteNonQuery() > 0
                ? FindById(connection, transaction, id)
                : null;
        });
    }

    /// <summary>淘汰最旧的非固定记录；固定条目不参与计数。返回被删除条目供调用方清理附件。</summary>
    private static IReadOnlyList<ClipboardItem> Evict(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int limit)
    {
        if (limit == EvictionPolicy.MinLimit)
        {
            return Array.Empty<ClipboardItem>();
        }

        var nonPinnedCount = CountNonPinned(connection, transaction);
        var excess = EvictionPolicy.ExcessCount(nonPinnedCount, limit);
        if (excess == 0)
        {
            return Array.Empty<ClipboardItem>();
        }

        var evicted = SelectOldestNonPinned(connection, transaction, excess);

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = """
            DELETE FROM clipboard_item WHERE id IN (
              SELECT id FROM clipboard_item
              WHERE pinned = 0 AND deleted_at IS NULL
              ORDER BY updated_at ASC, id ASC
              LIMIT $excess);
            """;
        delete.Parameters.AddWithValue("$excess", excess);
        delete.ExecuteNonQuery();

        return evicted;
    }

    private static int CountNonPinned(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM clipboard_item
            WHERE pinned = 0 AND deleted_at IS NULL;
            """;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<ClipboardItem> SelectOldestNonPinned(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int count)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM clipboard_item
            WHERE pinned = 0 AND deleted_at IS NULL
            ORDER BY updated_at ASC, id ASC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        using var reader = command.ExecuteReader();
        var items = new List<ClipboardItem>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static ClipboardItem? FindDuplicate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        string since,
        string? sourceWindow,
        bool matchSource)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM clipboard_item
            WHERE content_hash = $hash AND updated_at >= $since AND deleted_at IS NULL
              {(matchSource ? "AND source_window IS $source" : string.Empty)}
            ORDER BY updated_at DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue("$since", since);
        if (matchSource)
        {
            AddNullable(command, "$source", sourceWindow);
        }

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static ClipboardItem? FindById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SelectColumns} FROM clipboard_item WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static long Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ClipboardItem item,
        string updatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO clipboard_item
              (legacy_id, kind, text_content, file_path, thumb_path, url, mime, width, height,
               content_hash, normalized_text, pinned, pinned_at, note, tags, source_window,
               dup_count, created_at, updated_at, deleted_at, sources)
            VALUES
              ($legacyId, $kind, $textContent, $filePath, $thumbPath, $url, $mime, $width, $height,
               $contentHash, $normalizedText, $pinned, $pinnedAt, $note, $tags, $sourceWindow,
               $dupCount, $createdAt, $updatedAt, $deletedAt, $sources);
            SELECT last_insert_rowid();
            """;

        AddNullable(command, "$legacyId", item.LegacyId);
        command.Parameters.AddWithValue("$kind", ClipClassifierKeys.KindKey(item.Kind));
        AddNullable(command, "$textContent", item.TextContent);
        AddNullable(command, "$filePath", item.FilePath);
        AddNullable(command, "$thumbPath", item.ThumbPath);
        AddNullable(command, "$url", item.Url);
        AddNullable(command, "$mime", item.Mime);
        AddNullable(command, "$width", item.Width);
        AddNullable(command, "$height", item.Height);
        AddNullable(command, "$contentHash", item.ContentHash);
        AddNullable(command, "$normalizedText", item.NormalizedText);
        command.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
        AddNullable(command, "$pinnedAt", FormatTimestamp(item.PinnedAt));
        AddNullable(command, "$note", item.Note);
        command.Parameters.AddWithValue(
            "$tags",
            JsonSerializer.Serialize(item.Tags ?? Array.Empty<string>()));
        AddNullable(command, "$sourceWindow", item.SourceWindow);
        AddNullable(command, "$sources", FormatSources(MergeSources(item.SourceWindow, item.Sources)));
        command.Parameters.AddWithValue("$dupCount", Math.Max(1, item.DuplicateCount));
        command.Parameters.AddWithValue(
            "$createdAt",
            FormatTimestamp(item.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", updatedAt);
        AddNullable(
            command,
            "$deletedAt",
            FormatTimestamp(item.DeletedAt));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static ClipboardItem Map(SqliteDataReader reader)
    {
        return new ClipboardItem
        {
            Id = reader.GetInt64(0),
            LegacyId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Kind = ClipClassifierKeys.ParseKind(reader.GetString(2)),
            TextContent = reader.IsDBNull(3) ? null : reader.GetString(3),
            FilePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            ThumbPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            Url = reader.IsDBNull(6) ? null : reader.GetString(6),
            Mime = reader.IsDBNull(7) ? null : reader.GetString(7),
            Width = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Height = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            ContentHash = reader.IsDBNull(10) ? null : reader.GetString(10),
            NormalizedText = reader.IsDBNull(11) ? null : reader.GetString(11),
            Pinned = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
            PinnedAt = reader.IsDBNull(13)
                ? null
                : SqliteConventions.ParseTimestamp(reader.GetString(13)),
            Note = reader.IsDBNull(14) ? null : reader.GetString(14),
            Tags = ParseTags(reader.IsDBNull(15) ? null : reader.GetString(15)),
            SourceWindow = reader.IsDBNull(16) ? null : reader.GetString(16),
            Sources = ParseSources(
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(16) ? null : reader.GetString(16)),
            DuplicateCount = reader.IsDBNull(17) ? 1 : reader.GetInt32(17),
            CreatedAt = SqliteConventions.ParseTimestamp(reader.GetString(18)),
            UpdatedAt = SqliteConventions.ParseTimestamp(reader.GetString(19)),
            DeletedAt = reader.IsDBNull(20)
                ? null
                : SqliteConventions.ParseTimestamp(reader.GetString(20)),
        };
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        SqliteConventions.FormatTimestamp(value);

    private static string? FormatTimestamp(DateTimeOffset? value) =>
        SqliteConventions.FormatTimestamp(value);

    private static string? FormatSources(IReadOnlyList<string> sources) =>
        sources.Count == 0 ? null : JsonSerializer.Serialize(sources);

    private static IReadOnlyList<string> ParseSources(string? json, string? fallbackLatest)
    {
        var result = new List<string>(MaxSources);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                foreach (var source in JsonSerializer.Deserialize<string[]>(json)
                             ?? Array.Empty<string>())
                {
                    AddDistinctSource(result, source);
                }
            }
            catch (JsonException)
            {
                // 损坏值按“只有最新来源”处理。
            }
        }

        AddDistinctSource(result, fallbackLatest);
        return result;
    }

    private static IReadOnlyList<string> MergeSources(ClipboardItem existing, string? newSource) =>
        MergeSources(newSource, existing.Sources, existing.SourceWindow);

    private static IReadOnlyList<string> MergeSources(
        string? newSource,
        IReadOnlyList<string>? known,
        string? fallbackLatest = null)
    {
        var result = new List<string>(MaxSources);
        AddDistinctSource(result, newSource);
        if (known is not null)
        {
            foreach (var source in known)
            {
                AddDistinctSource(result, source);
            }
        }

        AddDistinctSource(result, fallbackLatest);
        return result;
    }

    private static void AddDistinctSource(List<string> sources, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || sources.Count >= MaxSources)
        {
            return;
        }

        var trimmed = value.Trim();
        if (sources.Any(source => string.Equals(source, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        sources.Add(trimmed);
    }

    private static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>仓储层使用的类型键映射，避免 Data 依赖 Services 的分类器实现。</summary>
internal static class ClipClassifierKeys
{
    public static string KindKey(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Link => "link",
        ClipboardKind.Image => "image",
        ClipboardKind.Html => "html",
        ClipboardKind.File => "file",
        _ => "text",
    };

    public static ClipboardKind ParseKind(string value) => value switch
    {
        "link" => ClipboardKind.Link,
        "image" => ClipboardKind.Image,
        "html" => ClipboardKind.Html,
        "file" => ClipboardKind.File,
        _ => ClipboardKind.Text,
    };
}
