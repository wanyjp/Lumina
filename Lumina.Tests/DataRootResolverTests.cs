using Lumina.Services.Configuration;

namespace Lumina.Tests;

/// <summary>P4-06 数据目录可写性检测与回退顺序。</summary>
public class DataRootResolverTests
{
    [Fact]
    public void Candidate_roots_start_with_current_and_include_local_and_temp()
    {
        var current = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

        var candidates = DataRootResolver.CandidateRoots(current);

        Assert.Equal(Path.GetFullPath(current), candidates[0]);
        Assert.Contains(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppPaths.AppFolderName),
            candidates);
        Assert.Contains(Path.Combine(Path.GetTempPath(), AppPaths.AppFolderName), candidates);
    }

    [Fact]
    public void Prepare_uses_primary_when_writable()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

        try
        {
            var preparation = DataRootResolver.Prepare([root], applyToEnvironment: false);

            Assert.False(preparation.UsedFallback);
            Assert.Null(preparation.Notice);
            Assert.Equal(Path.GetFullPath(root), preparation.Root);
            Assert.True(Directory.Exists(Path.Combine(root, "db")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Prepare_falls_back_to_next_writable_candidate_and_records_notice()
    {
        var blocked = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        var fallback = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
            File.WriteAllText(blocked, "not a directory");

            var preparation = DataRootResolver.Prepare([blocked, fallback], applyToEnvironment: false);

            Assert.True(preparation.UsedFallback);
            Assert.NotNull(preparation.Notice);
            Assert.Equal(Path.GetFullPath(fallback), preparation.Root);
            Assert.True(Directory.Exists(Path.Combine(fallback, "db")));
        }
        finally
        {
            File.Delete(blocked);
            if (Directory.Exists(fallback))
            {
                Directory.Delete(fallback, recursive: true);
            }
        }
    }
}
