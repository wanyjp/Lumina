namespace Lumina.Services.Configuration;

/// <summary>
/// 用户数据目录约定。v1.0 固定 `%APPDATA%\Lumina`，不支持用户改路径；
/// 单元测试或本地调试可通过环境变量 `LUMINA_DATA_DIR` 覆盖根目录。
/// 启动早期调用 <see cref="EnsureDirectories()"/> 建立固定目录结构。
/// </summary>
public static class AppPaths
{
    public const string ProductName = "拾光";
    public const string ProductNameEnglish = "Lumina";
    public const string AppFolderName = "Lumina";
    public const string DataRootOverrideVariable = "LUMINA_DATA_DIR";

    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataRootOverrideVariable);
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName)
                : overridden;
        }
    }

    public static string DatabaseDirectory => Path.Combine(Root, "db");

    public static string DatabaseFile => Path.Combine(DatabaseDirectory, "lumina.db");

    public static string FilesDirectory => Path.Combine(Root, "files");

    public static string ScreenshotsDirectory => Path.Combine(FilesDirectory, "screenshots");

    public static string ClipboardImagesDirectory => Path.Combine(FilesDirectory, "clipboard-images");

    public static string NotesDirectory => Path.Combine(FilesDirectory, "notes");

    public static string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>损坏数据库隔离目录：保留原文件，绝不静默删除。</summary>
    public static string CorruptDirectory => Path.Combine(Root, "corrupt");

    /// <summary>待应用恢复请求文件；下次启动在打开数据库前消费。</summary>
    public static string PendingRestoreFile => Path.Combine(Root, "pending-restore.json");

    public static string ConfigDirectory => Path.Combine(Root, "config");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string LogFile => Path.Combine(LogsDirectory, "app.log");

    public static void EnsureLogDirectory() => Directory.CreateDirectory(LogsDirectory);

    /// <summary>按当前根目录创建完整数据目录结构，返回规范化的根路径。</summary>
    public static string EnsureDirectories() => EnsureDirectories(Root);

    /// <summary>按指定根目录创建完整数据目录结构，便于测试与便携场景。</summary>
    public static string EnsureDirectories(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var rootPath = Path.GetFullPath(root);
        var directories = new[]
        {
            rootPath,
            Path.Combine(rootPath, "db"),
            Path.Combine(rootPath, "files", "screenshots"),
            Path.Combine(rootPath, "files", "clipboard-images"),
            Path.Combine(rootPath, "files", "notes"),
            Path.Combine(rootPath, "backups"),
            Path.Combine(rootPath, "corrupt"),
            Path.Combine(rootPath, "config"),
            Path.Combine(rootPath, "logs"),
        };

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }

        return rootPath;
    }
}
