using Lumina.Services.Configuration;

namespace Lumina.Tests;

/// <summary>数据目录覆盖与固定目录结构。</summary>
public class AppPathsTests
{
    [Fact]
    public void Ensure_directories_creates_fixed_layout_under_override_root()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataRootOverrideVariable);
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootOverrideVariable, root);

            Assert.Equal(root, AppPaths.Root);
            Assert.Equal(Path.GetFullPath(root), AppPaths.EnsureDirectories());

            var expectedDirectories = new[]
            {
                "db",
                Path.Combine("files", "screenshots"),
                Path.Combine("files", "clipboard-images"),
                Path.Combine("files", "notes"),
                "backups",
                "config",
                "logs",
            };

            foreach (var relative in expectedDirectories)
            {
                Assert.True(Directory.Exists(Path.Combine(root, relative)), relative);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootOverrideVariable, previous);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
