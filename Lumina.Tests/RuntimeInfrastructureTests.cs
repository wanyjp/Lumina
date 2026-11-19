using Lumina.Services.Configuration;
using Lumina.Services.Logging;
using Lumina.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Lumina.Tests;

/// <summary>单实例与结构化日志骨架的可执行验证。</summary>
public class RuntimeInfrastructureTests
{
    [Fact]
    public void Single_instance_guard_allows_only_one_owner()
    {
        var name = @"Local\Lumina.Tests." + Guid.NewGuid().ToString("N");

        using var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());

        bool secondAcquired = true;
        var thread = new Thread(() =>
        {
            using var second = new SingleInstanceGuard(name);
            secondAcquired = second.TryAcquire();
        });
        thread.Start();
        thread.Join();

        Assert.False(secondAcquired);
    }

    [Fact]
    public void Single_instance_guard_can_be_reacquired_after_release()
    {
        var name = @"Local\Lumina.Tests." + Guid.NewGuid().ToString("N");

        using (var first = new SingleInstanceGuard(name))
        {
            Assert.True(first.TryAcquire());
        }

        using var second = new SingleInstanceGuard(name);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void Rolling_file_logger_writes_one_json_line_per_entry()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-logs", Guid.NewGuid().ToString("N"));

        try
        {
            var options = new RollingFileLoggerOptions
            {
                Directory = directory,
                FileName = "test.log",
                MinimumLevel = LogLevel.Information,
            };

            using (var provider = new RollingFileLoggerProvider(options))
            {
                var logger = provider.CreateLogger("Runtime");
                logger.LogInformation("hello {Name}", "lumina");
            }

            var file = Path.Combine(directory, "test.log");
            Assert.True(File.Exists(file));

            var lines = File.ReadAllLines(file);
            Assert.Single(lines);
            Assert.Contains("hello lumina", lines[0]);
            Assert.Contains("Information", lines[0]);
            Assert.Contains("Runtime", lines[0]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void App_paths_do_not_require_real_user_profile_in_tests()
    {
        Assert.Equal("Lumina", AppPaths.AppFolderName);
        Assert.Equal("app.log", Path.GetFileName(AppPaths.LogFile));
    }
}
