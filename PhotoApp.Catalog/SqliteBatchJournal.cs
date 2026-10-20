using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoApp.Core.Batches;

namespace PhotoApp.Catalog;

/// <summary>Le journal des lots sur SQLite. Chaque méthode est une transaction courte : un plantage ne laisse jamais une ligne à moitié écrite.</summary>
public sealed class SqliteBatchJournal(CatalogDatabase database) : IBatchJournal
{
    private const string ItemColumns =
        "id, batch_id, rank, group_id, role, source_path, target_path, state, error, decided_date, " +
        "source_size, source_last_write_utc, source_creation_utc, done_size, done_last_write_utc";

    public long CreateBatch(DateTime createdUtc, IReadOnlyList<string> folders, IReadOnlyList<BatchItemDraft> items)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        long batchId;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO batch (created_utc, folders, state) VALUES ($created, $folders, $state); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$created", Text(createdUtc));
            insert.Parameters.AddWithValue("$folders", JsonSerializer.Serialize(folders));
            insert.Parameters.AddWithValue("$state", BatchState.Planned.ToString());
            batchId = (long)insert.ExecuteScalar()!;
        }

        using (var insertItem = connection.CreateCommand())
        {
            insertItem.Transaction = transaction;
            insertItem.CommandText =
                "INSERT INTO batch_item (batch_id, rank, group_id, role, source_path, target_path, state, decided_date, " +
                "source_size, source_last_write_utc, source_creation_utc) " +
                "VALUES ($batch, $rank, $group, $role, $source, $target, $state, $decided, $size, $write, $creation)";
            var batch = insertItem.Parameters.Add("$batch", SqliteType.Integer);
            var rank = insertItem.Parameters.Add("$rank", SqliteType.Integer);
            var group = insertItem.Parameters.Add("$group", SqliteType.Integer);
            var role = insertItem.Parameters.Add("$role", SqliteType.Text);
            var source = insertItem.Parameters.Add("$source", SqliteType.Text);
            var target = insertItem.Parameters.Add("$target", SqliteType.Text);
            var state = insertItem.Parameters.Add("$state", SqliteType.Text);
            var decided = insertItem.Parameters.Add("$decided", SqliteType.Text);
            var size = insertItem.Parameters.Add("$size", SqliteType.Integer);
            var write = insertItem.Parameters.Add("$write", SqliteType.Text);
            var creation = insertItem.Parameters.Add("$creation", SqliteType.Text);

            foreach (var item in items)
            {
                batch.Value = batchId;
                rank.Value = item.Rank;
                group.Value = item.GroupId;
                role.Value = item.Role.ToString();
                source.Value = item.SourcePath;
                target.Value = item.TargetPath;
                state.Value = ItemState.Pending.ToString();
                decided.Value = item.DecidedDate.ToString("O", CultureInfo.InvariantCulture);
                size.Value = item.SourceSize;
                write.Value = Text(item.SourceLastWriteUtc);
                creation.Value = Text(item.SourceCreationUtc);
                insertItem.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return batchId;
    }

    public BatchInfo? GetBatch(long batchId)
    {
        using var connection = database.Open();
        return QueryBatches(connection, "WHERE id = $id", ("$id", batchId)).FirstOrDefault();
    }

    public IReadOnlyList<BatchInfo> ListBatches()
    {
        using var connection = database.Open();
        return QueryBatches(connection, "ORDER BY id");
    }

    public BatchInfo? FindUnfinished()
    {
        using var connection = database.Open();
        return QueryBatches(connection, "WHERE state IN ('Running', 'Interrupted') ORDER BY id DESC LIMIT 1").FirstOrDefault();
    }

    public BatchInfo? FindLastUndoable()
    {
        using var connection = database.Open();
        return QueryBatches(
            connection,
            "WHERE state = 'Completed' AND EXISTS (SELECT 1 FROM batch_item i WHERE i.batch_id = batch.id AND i.state IN ('Done', 'UndoFailed')) " +
            "ORDER BY id DESC LIMIT 1").FirstOrDefault();
    }

    public IReadOnlyList<BatchItem> GetItems(long batchId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ItemColumns} FROM batch_item WHERE batch_id = $id ORDER BY rank";
        command.Parameters.AddWithValue("$id", batchId);

        var items = new List<BatchItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new BatchItem(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                Enum.Parse<ItemRole>(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<ItemState>(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                reader.GetInt64(10),
                ParseUtc(reader.GetString(11)),
                ParseUtc(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetInt64(13),
                reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14))));
        }

        return items;
    }

    public void SetBatchState(long batchId, BatchState state) =>
        Execute("UPDATE batch SET state = $state WHERE id = $id", ("$state", state.ToString()), ("$id", batchId));

    public void SetItemState(long itemId, ItemState state, string? error = null) =>
        Execute(
            "UPDATE batch_item SET state = $state, error = $error WHERE id = $id",
            ("$state", state.ToString()),
            ("$error", (object?)error ?? DBNull.Value),
            ("$id", itemId));

    public void SetItemDone(long itemId, long doneSize, DateTime doneLastWriteUtc, string? warning = null) =>
        Execute(
            "UPDATE batch_item SET state = 'Done', error = $error, done_size = $size, done_last_write_utc = $write WHERE id = $id",
            ("$error", (object?)warning ?? DBNull.Value),
            ("$size", doneSize),
            ("$write", Text(doneLastWriteUtc)),
            ("$id", itemId));

    public IDisposable AcquireExecutionLock()
    {
        var path = database.DatabasePath + ".lock";
        try
        {
            // Ouvert sans partage : un second processus échoue, et le système libère le verrou si celui-ci est tué.
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BatchLockedException($"Un autre traitement PhotoManager est en cours (verrou : {path}).", ex);
        }
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static List<BatchInfo> QueryBatches(SqliteConnection connection, string clause, params (string Name, object Value)[] parameters)
    {
        var rows = new List<(long Id, DateTime Created, IReadOnlyList<string> Folders, BatchState State)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, created_utc, folders, state FROM batch " + clause;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    ParseUtc(reader.GetString(1)),
                    JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
                    Enum.Parse<BatchState>(reader.GetString(3))));
            }
        }

        return rows.Select(row => new BatchInfo(row.Id, row.Created, row.Folders, row.State, CountsOf(connection, row.Id))).ToList();
    }

    private static BatchCounts CountsOf(SqliteConnection connection, long batchId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, COUNT(*) FROM batch_item WHERE batch_id = $id GROUP BY state";
        command.Parameters.AddWithValue("$id", batchId);

        var byState = new Dictionary<ItemState, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            byState[Enum.Parse<ItemState>(reader.GetString(0))] = reader.GetInt32(1);
        }

        int Get(ItemState state) => byState.GetValueOrDefault(state);
        return new BatchCounts(
            byState.Values.Sum(),
            Get(ItemState.Pending),
            Get(ItemState.Running),
            Get(ItemState.Done),
            Get(ItemState.Failed),
            Get(ItemState.Skipped),
            Get(ItemState.Undone),
            Get(ItemState.UndoFailed));
    }

    // Dates stockées en texte ISO 8601 aller-retour : les 100 ns d'un système de fichiers NTFS sont conservées.
    private static string Text(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
