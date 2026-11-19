using System.Globalization;
using Lumina.Data.Sqlite;
using Lumina.Domain.Stores;

namespace Lumina.Data.Focus;

/// <summary><c>daily_goal</c> 表读写（P3-01）：按本地自然日保存目标分钟数。</summary>
public sealed class DailyGoalRepository : IDailyGoalStore
{
    private readonly SqliteDatabase _database;

    public DailyGoalRepository(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public int GetTargetMinutes(DateOnly date) => _database.Read(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT target_minutes FROM daily_goal WHERE date = $date;";
        command.Parameters.AddWithValue("$date", Key(date));

        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    });

    public void SetTargetMinutes(DateOnly date, int minutes) =>
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO daily_goal (date, target_minutes)
                VALUES ($date, $minutes)
                ON CONFLICT(date) DO UPDATE SET target_minutes = excluded.target_minutes;
                """;
            command.Parameters.AddWithValue("$date", Key(date));
            command.Parameters.AddWithValue("$minutes", minutes);
            command.ExecuteNonQuery();
        });

    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
