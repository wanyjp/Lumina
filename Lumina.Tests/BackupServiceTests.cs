using Lumina.Data.Migrations;
using Lumina.Data.Todos;
using Lumina.Domain.Models;

namespace Lumina.Tests;

/// <summary>P3-10 数据导出与备份：备份文件生成后立即可读、可校验，恢复可回滚到快照。</summary>
public class BackupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Backup_file_is_released_and_valid_immediately()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var todos = new TodoRepository(temp.Database);
        todos.Insert(Todo("备份前待办"), Now);

        var path = temp.Backups.CreateBackup("acceptance", MigrationCatalog.LatestVersion);

        // 修复前连接池会占住备份文件，这里读取会抛 IOException。
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0);

        var validation = temp.Backups.Validate(path);
        Assert.True(validation.IsValid);
        Assert.Equal(MigrationCatalog.LatestVersion, validation.SchemaVersion);
        Assert.Null(validation.Error);
    }

    [Fact]
    public void Restore_rolls_back_to_snapshot_and_keeps_safety_copy()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var todos = new TodoRepository(temp.Database);
        todos.Insert(Todo("快照内待办"), Now);

        var path = temp.Backups.CreateBackup("pre-restore-test", MigrationCatalog.LatestVersion);
        todos.Insert(Todo("快照后待办"), Now);
        Assert.Equal(2, temp.CountRows("todo_item"));

        var safety = temp.Backups.Restore(path);

        Assert.NotNull(safety);
        Assert.True(File.Exists(safety));
        Assert.Equal(1, temp.CountRows("todo_item"));
        Assert.Equal("快照内待办", Assert.Single(todos.QueryAll()).Text);
    }

    private static TodoItem Todo(string text) => new()
    {
        Text = text,
        DueDate = new DateOnly(2026, 11, 14),
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
