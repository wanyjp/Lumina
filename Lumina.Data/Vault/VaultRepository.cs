using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Vault;

/// <summary>
/// vault_entry / vault_meta 仓储。密文原样存取，不做任何解密；
/// 分类以稳定字符串落库（login/bank/id/note），收藏另存布尔列。
/// </summary>
public sealed class VaultRepository : IVaultStore
{
    private const string EntryColumns = """
        id, category, title, username, enc_password, url, enc_note,
        favorite, created_at, updated_at, used_at
        """;

    private readonly SqliteDatabase _database;

    public VaultRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public VaultMetadata? ReadMetadata()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT kdf_salt, master_hash, kdf_iterations,
                       auto_lock_minutes, clear_clipboard_sec, last_locked_at,
                       recovery_question, recovery_answer_salt, recovery_blob, recovery_key_blob
                FROM vault_meta
                WHERE id = 1;
                """;

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new VaultMetadata
            {
                Salt = ReadBlob(reader, 0) ?? [],
                MasterHash = ReadBlob(reader, 1) ?? [],
                Iterations = reader.IsDBNull(2) ? 600_000 : reader.GetInt32(2),
                AutoLockMinutes = reader.IsDBNull(3) ? 5 : reader.GetInt32(3),
                ClearClipboardSeconds = reader.IsDBNull(4) ? 20 : reader.GetInt32(4),
                LastLockedAt = ParseTimestamp(reader, 5),
                RecoveryQuestion = reader.IsDBNull(6) ? null : reader.GetString(6),
                RecoveryAnswerSalt = ReadBlob(reader, 7),
                RecoveryBlob = ReadBlob(reader, 8),
                RecoveryKeyBlob = ReadBlob(reader, 9),
            };
        });
    }

    public void WriteMetadata(VaultMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO vault_meta
                  (id, kdf_salt, master_hash, auto_lock_minutes,
                   clear_clipboard_sec, last_locked_at, kdf_iterations,
                   recovery_question, recovery_answer_salt, recovery_blob, recovery_key_blob)
                VALUES
                  (1, $salt, $hash, $autoLock, $clearClipboard, $lastLocked, $iterations,
                   $recoveryQuestion, $recoverySalt, $recoveryBlob, $recoveryKeyBlob)
                ON CONFLICT(id) DO UPDATE SET
                  kdf_salt = excluded.kdf_salt,
                  master_hash = excluded.master_hash,
                  auto_lock_minutes = excluded.auto_lock_minutes,
                  clear_clipboard_sec = excluded.clear_clipboard_sec,
                  last_locked_at = excluded.last_locked_at,
                  kdf_iterations = excluded.kdf_iterations,
                  recovery_question = excluded.recovery_question,
                  recovery_answer_salt = excluded.recovery_answer_salt,
                  recovery_blob = excluded.recovery_blob,
                  recovery_key_blob = excluded.recovery_key_blob;
                """;

            command.Parameters.AddWithValue("$salt", metadata.Salt);
            command.Parameters.AddWithValue("$hash", metadata.MasterHash);
            command.Parameters.AddWithValue("$autoLock", metadata.AutoLockMinutes);
            command.Parameters.AddWithValue("$clearClipboard", metadata.ClearClipboardSeconds);
            command.Parameters.AddWithValue(
                "$lastLocked",
                (object?)FormatTimestamp(metadata.LastLockedAt) ?? DBNull.Value);
            command.Parameters.AddWithValue("$iterations", metadata.Iterations);
            command.Parameters.AddWithValue(
                "$recoveryQuestion",
                (object?)metadata.RecoveryQuestion ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$recoverySalt",
                (object?)metadata.RecoveryAnswerSalt ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$recoveryBlob",
                (object?)metadata.RecoveryBlob ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$recoveryKeyBlob",
                (object?)metadata.RecoveryKeyBlob ?? DBNull.Value);
            command.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<VaultEntry> QueryAll()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {EntryColumns} FROM vault_entry
                ORDER BY favorite DESC, updated_at DESC, id DESC;
                """;

            using var reader = command.ExecuteReader();
            var entries = new List<VaultEntry>();
            while (reader.Read())
            {
                entries.Add(Map(reader));
            }

            return entries;
        });
    }

    public VaultEntry? Find(long id)
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {EntryColumns} FROM vault_entry WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        });
    }

    public long Insert(VaultEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO vault_entry
                  (category, title, username, enc_password, url, enc_note,
                   favorite, created_at, updated_at, used_at)
                VALUES
                  ($category, $title, $username, $encPassword, $url, $encNote,
                   $favorite, $createdAt, $updatedAt, $usedAt);
                SELECT last_insert_rowid();
                """;

            Bind(command, entry);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(entry.CreatedAt ?? now));
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            command.Parameters.AddWithValue(
                "$usedAt",
                (object?)FormatTimestamp(entry.UsedAt) ?? DBNull.Value);

            return (long)(command.ExecuteScalar() ?? 0L);
        });
    }

    public void Update(VaultEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.Id);

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE vault_entry SET
                  category = $category,
                  title = $title,
                  username = $username,
                  enc_password = $encPassword,
                  url = $url,
                  enc_note = $encNote,
                  favorite = $favorite,
                  updated_at = $updatedAt,
                  used_at = $usedAt
                WHERE id = $id;
                """;

            Bind(command, entry);
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
            command.Parameters.AddWithValue(
                "$usedAt",
                (object?)FormatTimestamp(entry.UsedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
        });
    }

    public void UpdateEncryptedBatch(IReadOnlyList<VaultEntry> entries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        _database.ExecuteInTransaction((connection, transaction) =>
        {
            foreach (var entry in entries)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE vault_entry SET
                      enc_password = $encPassword,
                      enc_note = $encNote,
                      updated_at = $updatedAt
                    WHERE id = $id;
                    """;

                command.Parameters.AddWithValue(
                    "$encPassword",
                    (object?)entry.EncryptedPassword ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "$encNote",
                    (object?)entry.EncryptedNote ?? DBNull.Value);
                command.Parameters.AddWithValue("$id", entry.Id);
                command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(now));
                command.ExecuteNonQuery();
            }
        });
    }

    public void Delete(long id)
    {
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM vault_entry WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        });
    }

    public void MarkUsed(long id, DateTimeOffset at)
    {
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE vault_entry SET used_at = $at WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$at", FormatTimestamp(at));
            command.ExecuteNonQuery();
        });
    }

    public void MarkLocked(DateTimeOffset at)
    {
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE vault_meta SET last_locked_at = $at WHERE id = 1;";
            command.Parameters.AddWithValue("$at", FormatTimestamp(at));
            command.ExecuteNonQuery();
        });
    }

    private static void Bind(SqliteCommand command, VaultEntry entry)
    {
        command.Parameters.AddWithValue("$category", ToStorage(entry.Category));
        command.Parameters.AddWithValue("$title", (entry.Title ?? string.Empty).Trim());
        command.Parameters.AddWithValue(
            "$username",
            (object?)Normalize(entry.Username) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$encPassword",
            (object?)entry.EncryptedPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("$url", (object?)Normalize(entry.Url) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$encNote",
            (object?)entry.EncryptedNote ?? DBNull.Value);
        command.Parameters.AddWithValue("$favorite", entry.Favorite ? 1 : 0);
    }

    private static VaultEntry Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Category = FromStorage(reader.IsDBNull(1) ? null : reader.GetString(1)),
        Title = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
        Username = reader.IsDBNull(3) ? null : reader.GetString(3),
        EncryptedPassword = ReadBlob(reader, 4),
        Url = reader.IsDBNull(5) ? null : reader.GetString(5),
        EncryptedNote = ReadBlob(reader, 6),
        Favorite = !reader.IsDBNull(7) && reader.GetInt64(7) != 0,
        CreatedAt = ParseTimestamp(reader, 8),
        UpdatedAt = ParseTimestamp(reader, 9),
        UsedAt = ParseTimestamp(reader, 10),
    };

    private static byte[]? ReadBlob(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : (byte[])reader.GetValue(index);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatTimestamp(DateTimeOffset? value) =>
        SqliteConventions.FormatTimestamp(value);

    private static DateTimeOffset? ParseTimestamp(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        return SqliteConventions.ParseTimestamp(reader.GetString(index));
    }

    private static string ToStorage(VaultCategory category) => category switch
    {
        VaultCategory.BankCard => "bank",
        VaultCategory.IdDocument => "id",
        VaultCategory.SecureNote => "note",
        _ => "login",
    };

    private static VaultCategory FromStorage(string? value) => value switch
    {
        "bank" or "card" or "bankcard" => VaultCategory.BankCard,
        "id" or "document" => VaultCategory.IdDocument,
        "note" or "securenote" => VaultCategory.SecureNote,
        _ => VaultCategory.Login,
    };
}
