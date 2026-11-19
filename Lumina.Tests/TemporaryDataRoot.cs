using System.Globalization;
using Lumina.Data.Backup;
using Lumina.Data.Migrations;
using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Tests;

/// <summary>为数据层测试创建独立临时目录，避免触碰 %APPDATA% 与真实用户数据。</summary>
internal sealed class TemporaryDataRoot : IDisposable
{
    public TemporaryDataRoot()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(Root, "db", "lumina.db");
        BackupDirectory = Path.Combine(Root, "backups");
        QuarantineDirectory = Path.Combine(Root, "corrupt");
        FilesDirectory = Path.Combine(Root, "files");
        ConfigDirectory = Path.Combine(Root, "config");

        Directory.CreateDirectory(Path.Combine(Root, "db"));
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(QuarantineDirectory);
        Directory.CreateDirectory(Path.Combine(FilesDirectory, "screenshots"));
        Directory.CreateDirectory(Path.Combine(FilesDirectory, "clipboard-images"));
        Directory.CreateDirectory(ConfigDirectory);

        Database = new SqliteDatabase(DatabasePath);
        Backups = new DatabaseBackupService(Database, BackupDirectory);
        PendingStore = new PendingRestoreStore(Path.Combine(Root, "pending-restore.json"));
        PendingRestore = new PendingRestoreProcessor(Backups, PendingStore);
        Recovery = new DatabaseRecoveryService(Database, QuarantineDirectory);
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public string QuarantineDirectory { get; }

    public string FilesDirectory { get; }

    public string ConfigDirectory { get; }

    public SqliteDatabase Database { get; }

    public DatabaseBackupService Backups { get; }

    public PendingRestoreStore PendingStore { get; }

    public PendingRestoreProcessor PendingRestore { get; }

    public DatabaseRecoveryService Recovery { get; }

    public DatabaseInitializer CreateInitializer(IReadOnlyList<Migration>? migrations = null) =>
        new(Database, Backups, migrations ?? MigrationCatalog.All);

    public DatabaseInitializationResult Initialize() => CreateInitializer().Initialize();

    public int ReadSchemaVersion()
    {
        return Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public bool TableExists(string name)
    {
        return Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        });
    }

    public long CountRows(string table)
    {
        return Database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
