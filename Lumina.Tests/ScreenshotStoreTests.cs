using Lumina.Data.Screenshots;
using Lumina.Data.Settings;
using Lumina.Services.Capture;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P2-07 截图存储：入库、缩略图、上限淘汰、删除与缺失文件对账。</summary>
public class ScreenshotStoreTests
{
    private static readonly byte[] Png = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void Save_writes_original_and_thumbnail_and_records_metadata()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = CreateStore(temp);
        var captured = DateTimeOffset.Parse("2026-11-09T10:00:00+08:00");

        var record = store.Save(Png, "region", 1280, 720, captured);

        Assert.True(record.Id > 0);
        Assert.True(File.Exists(store.ResolvePath(record)));
        Assert.NotNull(record.ThumbPath);
        Assert.True(File.Exists(store.ResolvePath(record.ThumbPath!)));
        Assert.True(record.HasOriginal);

        var recent = store.Recent(10);
        Assert.Single(recent);
        Assert.Equal("region", recent[0].CaptureType);
        Assert.Equal(1280, recent[0].Width);
    }

    [Fact]
    public void Thumb_only_mode_removes_original_but_keeps_thumbnail()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = CreateStore(temp);

        var record = store.Save(Png, "full", 800, 600, DateTimeOffset.Now, keepOriginal: false);

        Assert.False(record.HasOriginal);
        Assert.Equal(record.FilePath, record.ThumbPath);
        Assert.True(File.Exists(store.ResolvePath(record)));
        Assert.Empty(Directory.GetFiles(store.StorageDirectory, "*.png"));
        Assert.Single(Directory.GetFiles(Path.Combine(store.StorageDirectory, "thumbs"), "*.png"));
    }

    [Fact]
    public void Enforce_limit_deletes_oldest_records_and_files()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = CreateStore(temp);
        var start = DateTimeOffset.Parse("2026-11-09T10:00:00+08:00");

        var first = store.Save(Png, "region", 100, 100, start);
        store.Save(Png, "region", 100, 100, start.AddMinutes(1));
        store.Save(Png, "region", 100, 100, start.AddMinutes(2));

        var deleted = store.EnforceLimit(2);

        Assert.Equal(1, deleted);
        Assert.Equal(2, store.Recent(10).Count);
        Assert.False(File.Exists(store.ResolvePath(first)));
    }

    [Fact]
    public void Delete_removes_record_and_files()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = CreateStore(temp);

        var record = store.Save(Png, "window", 320, 200, DateTimeOffset.Now);
        var original = store.ResolvePath(record);
        var thumb = store.ResolvePath(record.ThumbPath!);

        Assert.True(store.Delete(record.Id));
        Assert.Empty(store.Recent(10));
        Assert.False(File.Exists(original));
        Assert.False(File.Exists(thumb));
    }

    [Fact]
    public void Reconcile_removes_records_whose_file_is_missing()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var store = CreateStore(temp);

        var record = store.Save(Png, "delay", 640, 480, DateTimeOffset.Now);
        File.Delete(store.ResolvePath(record));

        var report = store.Reconcile();

        Assert.Equal(1, report.MissingRecords);
        Assert.Empty(store.Recent(10));
    }

    private static ScreenshotStore CreateStore(TemporaryDataRoot temp)
    {
        var files = new ScreenshotFileService(Path.Combine(temp.FilesDirectory, "screenshots"));
        var repository = new ScreenshotRepository(temp.Database);
        var settings = new SettingsStore(new SqliteSettingsBackend(temp.Database));

        // 缩略图工厂用恒等函数：本测试只关心文件与记录一致性，不验证图像缩放。
        return new ScreenshotStore(files, repository, settings, bytes => bytes);
    }
}
