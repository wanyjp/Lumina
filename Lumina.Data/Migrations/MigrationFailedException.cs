namespace Lumina.Data.Migrations;

/// <summary>迁移失败。事务已回滚，数据库保持在 <see cref="PreviousVersion"/>。</summary>
public sealed class MigrationFailedException : Exception
{
    public MigrationFailedException(
        int version,
        string description,
        int previousVersion,
        string? backupPath,
        Exception innerException)
        : base($"数据库迁移 v{version}（{description}）执行失败，已回滚到 v{previousVersion}。", innerException)
    {
        Version = version;
        Description = description;
        PreviousVersion = previousVersion;
        BackupPath = backupPath;
    }

    public int Version { get; }

    public string Description { get; }

    public int PreviousVersion { get; }

    public string? BackupPath { get; }
}
