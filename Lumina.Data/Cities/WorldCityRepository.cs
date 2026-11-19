using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;

namespace Lumina.Data.Cities;

/// <summary>world_city 表仓储：世界时钟城市读取、追加与删除。</summary>
public sealed class WorldCityRepository : IWorldCityStore
{
    private readonly SqliteDatabase _database;

    public WorldCityRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public IReadOnlyList<WorldCity> GetAll()
    {
        return _database.Read(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, iana_id, display_name, sort_order
                FROM world_city
                ORDER BY sort_order, id;
                """;
            using var reader = command.ExecuteReader();

            var cities = new List<WorldCity>();
            while (reader.Read())
            {
                cities.Add(new WorldCity(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3)));
            }

            return cities;
        });
    }

    public long Add(string ianaId, string displayName, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ianaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO world_city (iana_id, display_name, sort_order)
                VALUES ($ianaId, $displayName, $sortOrder);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$ianaId", ianaId);
            command.Parameters.AddWithValue("$displayName", displayName);
            command.Parameters.AddWithValue("$sortOrder", sortOrder);

            var id = command.ExecuteScalar();
            return Convert.ToInt64(id, CultureInfo.InvariantCulture);
        });
    }

    public void Remove(long id)
    {
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM world_city WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        });
    }
}
