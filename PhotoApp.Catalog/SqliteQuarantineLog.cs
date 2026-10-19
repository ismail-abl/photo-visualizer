using System.Globalization;
using PhotoApp.Core.Batches;

namespace PhotoApp.Catalog;

public sealed class SqliteQuarantineLog(CatalogDatabase database) : IQuarantineLog
{
    private const string Columns = "id, created_utc, original_path, stored_path, batch_id, state";

    public long Add(DateTime createdUtc, string originalPath, string storedPath, long? batchId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO quarantine_entry (created_utc, original_path, stored_path, batch_id, state) " +
            "VALUES ($created, $original, $stored, $batch, $state); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$created", createdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$original", originalPath);
        command.Parameters.AddWithValue("$stored", storedPath);
        command.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", QuarantineState.Held.ToString());
        return (long)command.ExecuteScalar()!;
    }

    public QuarantineEntry? Get(long id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM quarantine_entry WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<QuarantineEntry> List(QuarantineState? state = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM quarantine_entry " + (state is null ? string.Empty : "WHERE state = $state ") + "ORDER BY id";
        if (state is { } value)
        {
            command.Parameters.AddWithValue("$state", value.ToString());
        }

        var entries = new List<QuarantineEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(Read(reader));
        }

        return entries;
    }

    public void SetState(long id, QuarantineState state)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE quarantine_entry SET state = $state WHERE id = $id";
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static QuarantineEntry Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt64(0),
        DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetInt64(4),
        Enum.Parse<QuarantineState>(reader.GetString(5)));
}
