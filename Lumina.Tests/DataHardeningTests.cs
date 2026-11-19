using System.Text;
using Lumina.Data.Backup;
using Lumina.Data.Migrations;
using Lumina.Data.Todos;
using Lumina.Domain.Models;
using Microsoft.Data.Sqlite;

namespace Lumina.Tests;

/// <summary>
/// P4-04 数据加固：损坏库隔离重建、待恢复请求、损坏状态下用备份救回、外部导出。
/// </summary>
public class DataHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 17, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Corrupt_database_is_quarantined_and_reinitialized()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        new TodoRepository(temp.Database).Insert(Todo("损坏前待办"), Now);

        Corrupt(temp);

        var failure = Assert.ThrowsAny<Exception>(() => temp.Initialize());
        Assert.True(
            DatabaseRecoveryService.IsCorruption(failure),
            $"应判定为损坏，实际：{failure.GetType().Name}: {failure.Message}");
        Assert.True(temp.Recovery.HasRecoverableDatabase);

        var quarantined = temp.Recovery.Quarantine();

        Assert.True(File.Exists(quarantined));
        Assert.Equal("this is not a sqlite database", File.ReadAllText(quarantined));
        Assert.False(File.Exists(temp.DatabasePath));

        var result = temp.Initialize();

        Assert.Equal(MigrationCatalog.LatestVersion, result.CurrentVersion);
        Assert.Equal(0, temp.CountRows("todo_item"));
    }

    [Fact]
    public void Pending_restore_applies_at_startup_and_keeps_safety_snapshot()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var todos = new TodoRepository(temp.Database);
        todos.Insert(Todo("快照内待办"), Now);

        var backup = temp.Backups.CreateBackup("manual-export", MigrationCatalog.LatestVersion);
        todos.Insert(Todo("快照后新增"), Now);
        Assert.Equal(2, temp.CountRows("todo_item"));

        temp.PendingStore.Write(backup);
        var outcome = temp.PendingRestore.ApplyIfPending();

        Assert.True(outcome.HadRequest);
        Assert.True(outcome.Applied);
        Assert.Equal(MigrationCatalog.LatestVersion, outcome.SchemaVersion);
        Assert.NotNull(outcome.SafetyBackupPath);
        Assert.True(File.Exists(outcome.SafetyBackupPath));
        Assert.Null(temp.PendingStore.Read());

        Assert.Equal(1, temp.CountRows("todo_item"));
        Assert.Equal("快照内待办", Assert.Single(new TodoRepository(temp.Database).QueryAll()).Text);
    }

    [Fact]
    public void Pending_restore_with_missing_file_is_rejected_and_cleared()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var missing = Path.Combine(temp.Root, "backups", "not-found.db");
        temp.PendingStore.Write(missing);

        var outcome = temp.PendingRestore.ApplyIfPending();

        Assert.True(outcome.HadRequest);
        Assert.False(outcome.Applied);
        Assert.Contains("不存在", outcome.Error, StringComparison.Ordinal);
        Assert.Null(temp.PendingStore.Read());
        Assert.Equal(MigrationCatalog.LatestVersion, temp.ReadSchemaVersion());
    }

    [Fact]
    public void Restore_over_corrupt_database_preserves_old_file()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        new TodoRepository(temp.Database).Insert(Todo("可救回待办"), Now);
        var backup = temp.Backups.CreateBackup("manual-export", MigrationCatalog.LatestVersion);

        Corrupt(temp);

        var safety = temp.Backups.Restore(backup);

        Assert.Null(safety);
        var preserved = Directory.GetFiles(
            Path.GetDirectoryName(temp.DatabasePath)!,
            "lumina.db.pre-restore-*");
        Assert.Single(preserved);
        Assert.True(new FileInfo(preserved[0]).Length > 0);

        Assert.Equal(MigrationCatalog.LatestVersion, temp.ReadSchemaVersion());
        Assert.Equal(1, temp.CountRows("todo_item"));
    }

    [Fact]
    public void Pending_restore_rejects_backup_from_newer_schema()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        new TodoRepository(temp.Database).Insert(Todo("高版本备份"), Now);
        var backup = temp.Backups.CreateBackup("future", 9);

        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = backup,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_version SET version = 99 WHERE version = (SELECT MAX(version) FROM schema_version);";
            command.ExecuteNonQuery();
        }

        Assert.Equal(99, temp.Backups.Validate(backup).SchemaVersion);
        temp.PendingStore.Write(backup);

        var outcome = temp.PendingRestore.ApplyIfPending();

        Assert.True(outcome.HadRequest);
        Assert.False(outcome.Applied);
        Assert.Contains("更高版本", outcome.Error, StringComparison.Ordinal);
        Assert.Null(temp.PendingStore.Read());
        Assert.Equal(1, temp.CountRows("todo_item"));
    }

    [Fact]
    public void Export_to_external_path_is_valid_and_released()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        new TodoRepository(temp.Database).Insert(Todo("导出待办"), Now);

        var target = Path.Combine(temp.Root, "export", "manual-backup.db");
        temp.Backups.BackupTo(target);

        Assert.True(File.Exists(target));
        var validation = temp.Backups.Validate(target);
        Assert.True(validation.IsValid);
        Assert.Equal(MigrationCatalog.LatestVersion, validation.SchemaVersion);
    }

    [Fact]
    public void Corrupt_pending_restore_file_is_ignored()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        File.WriteAllText(temp.PendingStore.FilePath, "{ not-valid-json", Encoding.UTF8);

        Assert.Null(temp.PendingStore.Read());
        var outcome = temp.PendingRestore.ApplyIfPending();

        Assert.False(outcome.HadRequest);
        Assert.False(outcome.Applied);
        Assert.Equal(MigrationCatalog.LatestVersion, temp.ReadSchemaVersion());
    }

    private static void Corrupt(TemporaryDataRoot temp)
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = temp.DatabasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }

        File.WriteAllText(temp.DatabasePath, "this is not a sqlite database");
    }

    private static TodoItem Todo(string text) => new()
    {
        Text = text,
        DueDate = new DateOnly(2026, 11, 15),
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
