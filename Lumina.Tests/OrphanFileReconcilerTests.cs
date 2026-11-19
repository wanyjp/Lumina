using Lumina.Data.Files;

namespace Lumina.Tests;

/// <summary>启动对账：识别孤儿文件与缺失文件，默认不删除。</summary>
public class OrphanFileReconcilerTests
{
    [Fact]
    public void Reconcile_reports_orphans_and_missing_files_without_deleting()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var referenced = Path.Combine(temp.FilesDirectory, "clipboard-images", "ref.png");
        var absolute = Path.Combine(temp.FilesDirectory, "clipboard-images", "abs.png");
        var orphan = Path.Combine(temp.FilesDirectory, "screenshots", "orphan.png");
        File.WriteAllBytes(referenced, [1, 2, 3]);
        File.WriteAllBytes(absolute, [5, 6]);
        File.WriteAllBytes(orphan, [4]);

        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clipboard_item(kind, file_path, created_at, updated_at)
                VALUES ('image', 'clipboard-images/ref.png', '2026-11-05T00:00:00+08:00', '2026-11-05T00:00:00+08:00');
                INSERT INTO clipboard_item(kind, file_path, created_at, updated_at)
                VALUES ('image', $absolute, '2026-11-05T00:00:00+08:00', '2026-11-05T00:00:00+08:00');
                INSERT INTO screenshot_meta(file_path, created_at)
                VALUES ('screenshots/missing.png', '2026-11-05T00:00:00+08:00');
                """;
            command.Parameters.AddWithValue("$absolute", absolute);
            command.ExecuteNonQuery();
        });

        var reconciler = new OrphanFileReconciler(temp.Database, temp.FilesDirectory);
        var report = reconciler.Reconcile();

        Assert.Contains("screenshots/orphan.png", report.OrphanFiles);
        Assert.Contains("screenshots/missing.png", report.MissingFiles);
        Assert.DoesNotContain("clipboard-images/ref.png", report.OrphanFiles);
        Assert.DoesNotContain("clipboard-images/abs.png", report.OrphanFiles);
        Assert.Empty(report.DeletedFiles);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void Reconcile_can_delete_orphans_when_explicitly_requested()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var orphan = Path.Combine(temp.FilesDirectory, "screenshots", "orphan.png");
        File.WriteAllBytes(orphan, [4]);

        var report = new OrphanFileReconciler(temp.Database, temp.FilesDirectory)
            .Reconcile(deleteOrphanFiles: true);

        Assert.Contains("screenshots/orphan.png", report.DeletedFiles);
        Assert.Empty(report.OrphanFiles);
        Assert.False(File.Exists(orphan));
    }
}
