using System.IO;
using Lumina.Services.Configuration;

namespace Lumina.Services.Clipboard;

/// <summary>
/// 剪贴板图片路径解析：数据库可能保存绝对路径或相对 `files/` 的路径（含历史
/// 遗留的 `files/xxx` 前缀），统一解析为可读取的绝对路径；不存在时返回 null。
/// </summary>
public static class ClipboardMedia
{
    public static string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = path.Trim();

        if (Path.IsPathRooted(value))
        {
            return TryOwned(value);
        }

        foreach (var candidate in Candidates(value))
        {
            if (TryOwned(candidate) is { } owned)
            {
                return owned;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string relative)
    {
        // 归一化 `..` 后再拼接，避免 `a/../../x` 越出数据目录。
        var normalized = relative.Replace('\\', '/').TrimStart('.', '/');

        yield return Path.Combine(
            AppPaths.FilesDirectory,
            normalized.Replace('/', Path.DirectorySeparatorChar));

        yield return Path.Combine(
            AppPaths.Root,
            normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 只接受位于文件目录内的绝对路径：数据库中的异常路径不得读取数据目录之外的文件。
    /// </summary>
    private static string? TryOwned(string candidate)
    {
        try
        {
            var root = Path.GetFullPath(AppPaths.FilesDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
