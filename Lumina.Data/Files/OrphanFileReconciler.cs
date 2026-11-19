using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Data.Files;

/// <summary>孤儿文件对账结果；路径均为相对 <c>files/</c> 的规范化路径。</summary>
public sealed record OrphanReconciliationReport(
    IReadOnlyList<string> OrphanFiles,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> DeletedFiles);

/// <summary>
/// 启动时对账：把截图与剪贴板目录中的实际文件同数据库记录比对。
/// 默认只报告不删除；删除开关仅由后续维护入口显式开启。
/// </summary>
public sealed class OrphanFileReconciler
{
    private static readonly string[] ManagedDirectories = ["screenshots", "clipboard-images"];

    private readonly SqliteDatabase _database;
    private readonly string _filesDirectory;

    public OrphanFileReconciler(SqliteDatabase database, string filesDirectory)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);

        _database = database;
        _filesDirectory = Path.GetFullPath(filesDirectory);
    }

    public OrphanReconciliationReport Reconcile(bool deleteOrphanFiles = false)
    {
        var referenced = LoadReferencedPaths();
        var onDisk = ScanManagedFiles();

        var orphanFiles = onDisk
            .Where(path => !referenced.Contains(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingFiles = referenced
            .Where(path => !File.Exists(Resolve(path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var deletedFiles = new List<string>();
        if (deleteOrphanFiles)
        {
            foreach (var orphan in orphanFiles)
            {
                var fullPath = Resolve(orphan);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    File.Delete(fullPath);
                    deletedFiles.Add(orphan);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个文件被占用不应中断整体对账：保留在孤儿列表中下次再试。
                }
            }

            orphanFiles = orphanFiles.Except(deletedFiles, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return new OrphanReconciliationReport(orphanFiles, missingFiles, deletedFiles);
    }

    private HashSet<string> LoadReferencedPaths()
    {
        return _database.Read(connection =>
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddPaths(connection, paths, "SELECT file_path FROM screenshot_meta");
            AddPaths(connection, paths, "SELECT thumb_path FROM screenshot_meta");
            AddPaths(connection, paths, "SELECT file_path FROM clipboard_item");
            AddPaths(connection, paths, "SELECT thumb_path FROM clipboard_item");
            return paths;
        });
    }

    private void AddPaths(SqliteConnection connection, HashSet<string> paths, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var value = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = NormalizeReferenced(value);
            if (normalized is not null)
            {
                paths.Add(normalized);
            }
        }
    }

    /// <summary>
    /// 数据库可能保存相对 `files/` 的路径，也可能保存剪贴板图片的绝对路径；
    /// 统一换算为相对路径后再与磁盘扫描结果比对，避免把仍被引用的图片误判为孤儿。
    /// </summary>
    private string? NormalizeReferenced(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return NormalizeRelative(path);
        }

        var fullPath = Path.GetFullPath(path);
        var root = _filesDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeRelative(Path.GetRelativePath(_filesDirectory, fullPath));
    }

    private HashSet<string> ScanManagedFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directoryName in ManagedDirectories)
        {
            var directory = Path.Combine(_filesDirectory, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                files.Add(NormalizeRelative(Path.GetRelativePath(_filesDirectory, file)));
            }
        }

        return files;
    }

    private string Resolve(string relativePath) =>
        Path.Combine(_filesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string NormalizeRelative(string path) =>
        path.Replace('\\', '/').TrimStart('.', '/');
}
