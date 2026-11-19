using Lumina.Data.Migrations;

namespace Lumina.Tests;

/// <summary>迁移执行器验收：首建库、幂等重入、失败回滚与迁移前备份。</summary>
public class MigrationTests
{
    private static readonly string[] CoreTables =
    [
        "app_setting",
        "shortcut_binding",
        "world_city",
        "schedule_event",
        "todo_item",
        "clipboard_item",
        "screenshot_meta",
        "countdown_item",
        "focus_session",
        "daily_goal",
        "note_item",
        "vault_entry",
        "vault_meta",
        "reminder_dedup",
    ];

    [Fact]
    public void Fresh_database_creates_all_core_tables()
    {
        using var temp = new TemporaryDataRoot();

        var result = temp.Initialize();

        Assert.Equal(0, result.PreviousVersion);
        Assert.Equal(MigrationCatalog.LatestVersion, result.CurrentVersion);
        Assert.Equal(MigrationCatalog.LatestVersion - MigrationCatalog.InitialVersion + 1, result.AppliedCount);
        Assert.Null(result.BackupPath);
        Assert.True(result.Fts5Available);

        var tables = temp.Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = command.ExecuteReader();

            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        });

        Assert.Contains("schema_version", tables);
        foreach (var expected in CoreTables)
        {
            Assert.Contains(expected, tables);
        }

        Assert.Equal(15, tables.Count);
    }

    [Fact]
    public void Vault_meta_tracks_kdf_iterations()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var columns = temp.Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(vault_meta);";
            using var reader = command.ExecuteReader();

            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(1));
            }

            return names;
        });

        Assert.Contains("kdf_iterations", columns);
    }

    [Fact]
    public void Database_uses_wal_journal_mode()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var journalMode = temp.Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            return (command.ExecuteScalar() as string ?? string.Empty).ToLowerInvariant();
        });

        Assert.Equal("wal", journalMode);
    }

    [Fact]
    public void Repeated_initialization_is_idempotent()
    {
        using var temp = new TemporaryDataRoot();

        var first = temp.Initialize();
        var second = temp.Initialize();

        Assert.Equal(first.CurrentVersion, second.PreviousVersion);
        Assert.Equal(first.CurrentVersion, second.CurrentVersion);
        Assert.Equal(0, second.AppliedCount);
        Assert.Null(second.BackupPath);
        Assert.Equal(MigrationCatalog.LatestVersion, temp.CountRows("schema_version"));
    }

    [Fact]
    public void Failed_migration_rolls_back_and_keeps_previous_version()
    {
        using var temp = new TemporaryDataRoot();
        var migrations = new[]
        {
            MigrationCatalog.All[0],
            new Migration
            {
                Version = 2,
                Description = "故意失败的迁移",
                Sql = """
                    CREATE TABLE migration_probe (id INTEGER PRIMARY KEY);
                    INSERT INTO table_that_does_not_exist VALUES (1);
                    """,
            },
        };

        var exception = Assert.Throws<MigrationFailedException>(
            () => temp.CreateInitializer(migrations).Initialize(TestContext.Current.CancellationToken));

        Assert.Equal(2, exception.Version);
        Assert.Equal(1, exception.PreviousVersion);
        Assert.False(temp.TableExists("migration_probe"));
        Assert.Equal(1, temp.ReadSchemaVersion());
        Assert.Equal(1, temp.CountRows("schema_version"));
    }

    [Fact]
    public void Pending_migration_creates_backup_and_backup_can_be_restored()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO app_setting(key, value, module, updated_at)
                VALUES ('probe', '"before"', NULL, '2026-11-05T00:00:00+08:00');
                """;
            command.ExecuteNonQuery();
        });

        var pendingVersion = MigrationCatalog.LatestVersion + 1;
        var migrations = MigrationCatalog.All
            .Append(new Migration
            {
                Version = pendingVersion,
                Description = "新增验证表",
                Sql = "CREATE TABLE migration_probe (id INTEGER PRIMARY KEY);",
            })
            .ToArray();

        var result = temp.CreateInitializer(migrations).Initialize(TestContext.Current.CancellationToken);

        Assert.Equal(MigrationCatalog.LatestVersion, result.PreviousVersion);
        Assert.Equal(pendingVersion, result.CurrentVersion);
        Assert.Equal(1, result.AppliedCount);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains("pre-migration", Path.GetFileName(result.BackupPath), StringComparison.Ordinal);

        var validation = temp.Backups.Validate(result.BackupPath!);
        Assert.True(validation.IsValid);
        Assert.Equal(MigrationCatalog.LatestVersion, validation.SchemaVersion);

        var safetyBackup = temp.Backups.Restore(result.BackupPath!);

        Assert.NotNull(safetyBackup);
        Assert.True(File.Exists(safetyBackup));
        Assert.Equal(MigrationCatalog.LatestVersion, temp.ReadSchemaVersion());
        Assert.False(temp.TableExists("migration_probe"));

        var restoredValue = temp.Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_setting WHERE key = 'probe';";
            return command.ExecuteScalar() as string;
        });
        Assert.Equal("\"before\"", restoredValue);
    }

    [Fact]
    public void Database_newer_than_application_is_rejected()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schema_version(version, applied_at, description)
                VALUES (99, '2026-11-05T00:00:00+08:00', '未来版本');
                """;
            command.ExecuteNonQuery();
        });

        var exception = Assert.Throws<InvalidOperationException>(() => temp.Initialize());
        Assert.Contains("v99", exception.Message);
    }
}
