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
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();
        using var migration = connection.BeginTransaction();
        command.Transaction = migration;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS readings (
                id TEXT PRIMARY KEY,
                machine_id TEXT NOT NULL,
                recorded_at INTEGER NOT NULL,
                temperature_c REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS readings_latest ON readings (recorded_at DESC, id DESC);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = """
                ALTER TABLE readings ADD COLUMN threshold_c REAL;
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        migration.Commit();
    }

    public void Save(Reading reading, double? thresholdC)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO readings (id, machine_id, recorded_at, temperature_c, threshold_c)
            VALUES ($id, $machine, $time, $temperature, $threshold)
            ON CONFLICT (id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", reading.Id.ToString("D"));
        command.Parameters.AddWithValue("$machine", reading.MachineId);
        command.Parameters.AddWithValue("$time", reading.RecordedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$temperature", reading.TemperatureC);
        command.Parameters.AddWithValue("$threshold", (object?)thresholdC ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<(Reading Reading, double? ThresholdC)> Latest()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, machine_id, recorded_at, temperature_c, threshold_c
            FROM readings
            ORDER BY recorded_at DESC, id DESC
            LIMIT 50;
            """;
        using var rows = command.ExecuteReader();
        var result = new List<(Reading, double?)>();
        while (rows.Read())
        {
            var reading = new Reading(
                Guid.Parse(rows.GetString(0)), rows.GetString(1),
                DateTimeOffset.FromUnixTimeMilliseconds(rows.GetInt64(2)), rows.GetDouble(3));
            result.Add((reading, rows.IsDBNull(4) ? null : rows.GetDouble(4)));
        }
        return result;
    }

}
