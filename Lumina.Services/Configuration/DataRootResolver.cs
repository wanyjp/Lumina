using System.IO;

namespace Lumina.Services.Configuration;

/// <summary>数据根目录准备结果：实际使用的目录、是否发生回退、给用户的提示。</summary>
public sealed record DataRootPreparation(string Root, bool UsedFallback, string? Notice);

/// <summary>
/// 启动早期检测数据目录可写性：默认使用 <see cref="AppPaths.Root"/>；
/// 不可写时依次回退到本地应用数据目录与临时目录，并通过环境变量把结果
/// 固定给本次进程（<see cref="AppPaths"/> 其余成员随之切换）。
/// </summary>
public static class DataRootResolver
{
    /// <summary>按优先级给出候选根目录：当前目录 → 本地应用数据 → 临时目录。</summary>
    public static IReadOnlyList<string> CandidateRoots(string? currentRoot = null)
    {
        var roots = new List<string>();
        Add(currentRoot ?? AppPaths.Root);
        Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppPaths.AppFolderName));
        Add(Path.Combine(Path.GetTempPath(), AppPaths.AppFolderName));
        return roots;

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(fullPath);
            }
        }
    }

    public static DataRootPreparation Prepare() => Prepare(CandidateRoots());

    /// <summary>
    /// 依次尝试候选目录；第一个可写目录成为本次进程的数据根。
    /// 回退时写入 <see cref="AppPaths.DataRootOverrideVariable"/>，保证后续所有路径一致。
    /// </summary>
    public static DataRootPreparation Prepare(
        IReadOnlyList<string> candidates,
        bool applyToEnvironment = true)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("至少需要一个候选数据目录。", nameof(candidates));
        }

        var primary = Path.GetFullPath(candidates[0]);
        var failures = new List<string>();

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            try
            {
                AppPaths.EnsureDirectories(fullPath);
                ProbeWritable(fullPath);

                var usedFallback = !string.Equals(fullPath, primary, StringComparison.OrdinalIgnoreCase);
                if (usedFallback && applyToEnvironment)
                {
                    Environment.SetEnvironmentVariable(AppPaths.DataRootOverrideVariable, fullPath);
                }

                var notice = usedFallback
                    ? $"原数据目录不可写：{primary}（{failures[0]}）。本次已改用 {fullPath}；"
                        + "请检查目录权限或磁盘空间，修复后重启拾光即可恢复原位置。"
                    : null;
                return new DataRootPreparation(fullPath, usedFallback, notice);
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
            {
                failures.Add(ex.Message);
            }
        }

        throw new IOException(
            "无法创建可写的数据目录。已尝试：" + string.Join("；", candidates)
            + "。最后错误：" + failures[^1]);
    }

    /// <summary>在目录内创建并立即删除一个探针文件，验证真实写入权限。</summary>
    private static void ProbeWritable(string root)
    {
        var probe = Path.Combine(root, $".write-probe-{Guid.NewGuid():N}");
        using var stream = new FileStream(
            probe,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }
}
