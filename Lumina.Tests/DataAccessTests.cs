using Lumina.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lumina.Tests;

/// <summary>连接、事务与仓储基类的基础行为。</summary>
public class DataAccessTests
{
    [Fact]
    public void Read_only_connection_rejects_writes()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        using var connection = temp.Database.OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting(key, value, module, updated_at)
            VALUES ('probe', '"x"', NULL, '2026-11-05T00:00:00+08:00');
            """;

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Equal(0, temp.CountRows("app_setting"));
    }

    [Fact]
    public void Transaction_wrapper_rolls_back_on_exception()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            temp.Database.ExecuteInTransaction((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO app_setting(key, value, module, updated_at)
                    VALUES ('probe', '"x"', NULL, '2026-11-05T00:00:00+08:00');
                    """;
                command.ExecuteNonQuery();

                throw new InvalidOperationException("boom");
            }));

        Assert.Contains("boom", exception.Message);
        Assert.Equal(0, temp.CountRows("app_setting"));
    }

    [Fact]
    public void Repository_base_reads_and_writes_through_database()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();

        var repository = new ProbeRepository(temp.Database);
        repository.Insert("probe");

        Assert.Equal(1, repository.Count());
    }

    [Fact]
    public void Backup_validation_rejects_non_database_file()
    {
        using var temp = new TemporaryDataRoot();
        var brokenPath = Path.Combine(temp.BackupDirectory, "broken.db");
        File.WriteAllText(brokenPath, "not a sqlite database");

        var result = temp.Backups.Validate(brokenPath);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    private sealed class ProbeRepository : RepositoryBase
    {
        public ProbeRepository(SqliteDatabase database) : base(database)
        {
        }

        public long Count()
        {
            return Read(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM app_setting;";
                return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            });
        }

        public void Insert(string key)
        {
            Write((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO app_setting(key, value, module, updated_at)
                    VALUES ($key, '"probe"', NULL, '2026-11-05T00:00:00+08:00');
                    """;
                command.Parameters.AddWithValue("$key", key);
                command.ExecuteNonQuery();
            });
        }
    }
}
