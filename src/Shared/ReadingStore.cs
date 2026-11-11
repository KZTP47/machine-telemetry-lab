using Microsoft.Data.Sqlite;

namespace MachineTelemetry;

public sealed class ReadingStore
{
    private readonly string connectionString;

    public ReadingStore(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS readings (
                id TEXT PRIMARY KEY,
                machine_id TEXT NOT NULL,
                recorded_at INTEGER NOT NULL,
                temperature_c REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS readings_latest ON readings (recorded_at DESC, id DESC);
            """;
        command.ExecuteNonQuery();
    }

    public void Save(Reading reading)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO readings (id, machine_id, recorded_at, temperature_c)
            VALUES ($id, $machine, $time, $temperature)
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", reading.Id.ToString("D"));
        command.Parameters.AddWithValue("$machine", reading.MachineId);
        command.Parameters.AddWithValue("$time", reading.RecordedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$temperature", reading.TemperatureC);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Reading> Latest()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, machine_id, recorded_at, temperature_c
            FROM readings
            ORDER BY recorded_at DESC, id DESC
            LIMIT 50;
            """;
        using var rows = command.ExecuteReader();
        var result = new List<Reading>();
        while (rows.Read())
        {
            result.Add(new Reading(
                Guid.Parse(rows.GetString(0)), rows.GetString(1),
                DateTimeOffset.FromUnixTimeMilliseconds(rows.GetInt64(2)), rows.GetDouble(3)));
        }
        return result;
    }

}
