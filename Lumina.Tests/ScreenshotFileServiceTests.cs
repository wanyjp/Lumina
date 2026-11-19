using Lumina.Services.Capture;

namespace Lumina.Tests;

/// <summary>P2-04 截图落盘策略：文件名格式、目录创建、同毫秒冲突不覆盖。</summary>
public class ScreenshotFileServiceTests
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void BuildFileName_uses_local_timestamp_and_png()
    {
        var captured = new DateTimeOffset(2026, 11, 8, 16, 30, 5, 123, TimeSpan.FromHours(8));

        Assert.Equal("shot_20261108_163005123.png", ScreenshotFileService.BuildFileName(captured));
    }

    [Fact]
    public void SavePng_creates_directory_and_writes_bytes()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new ScreenshotFileService(directory);
            var path = service.SavePng([1, 2, 3, 4], DateTimeOffset.Now);

            Assert.StartsWith(directory, path);
            Assert.True(File.Exists(path));
            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavePng_appends_sequence_on_name_collision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new ScreenshotFileService(directory);
            var captured = new DateTimeOffset(2026, 11, 8, 16, 30, 5, 123, TimeSpan.FromHours(8));

            var first = service.SavePng([1], captured);
            var second = service.SavePng([2], captured);
            var third = service.SavePng([3], captured);

            Assert.Equal("shot_20261108_163005123.png", Path.GetFileName(first));
            Assert.Equal("shot_20261108_163005123_2.png", Path.GetFileName(second));
            Assert.Equal("shot_20261108_163005123_3.png", Path.GetFileName(third));
            Assert.All(new[] { first, second, third }, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavePng_rejects_empty_payload()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new ScreenshotFileService(directory);

            Assert.Throws<ArgumentException>(() => service.SavePng([], DateTimeOffset.Now));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavePng_reports_storage_directory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new ScreenshotFileService(directory);

            Assert.Equal(directory, service.StorageDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_rejects_blank_directory()
    {
        Assert.Throws<ArgumentException>(() => new ScreenshotFileService("  "));
    }
}
