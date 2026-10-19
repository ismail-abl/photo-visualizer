namespace PhotoApp.Tests;

public sealed class CatalogDatabaseTests
{
    [Fact]
    public void Constructor_CreatesTheSchema_AndRecordsItsVersion()
    {
        using var temp = new TempDirectory();
        var database = new CatalogDatabase(temp.Combine("catalog.db"));

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version";
        Assert.Equal(CatalogDatabase.SchemaVersion, Convert.ToInt32(command.ExecuteScalar()));
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('batch', 'batch_item', 'quarantine_entry')";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(["batch", "batch_item", "quarantine_entry"], tables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Constructor_OnAnExistingDatabase_KeepsItsContent()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("catalog.db");
        var first = new CatalogDatabase(path);
        using (var connection = first.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO batch (created_utc, folders, state) VALUES ('2026-10-19T10:00:00.0000000Z', '[]', 'Completed')";
            command.ExecuteNonQuery();
        }

        var second = new CatalogDatabase(path);

        using var reopened = second.Open();
        using var count = reopened.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM batch";
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public void Constructor_MissingFolder_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<DirectoryNotFoundException>(() => new CatalogDatabase(temp.Combine("absent", "catalog.db")));
    }

    [Fact]
    public void Constructor_DatabaseFromAFutureVersion_Throws()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("catalog.db");
        var database = new CatalogDatabase(path);
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"UPDATE schema_version SET version = {CatalogDatabase.SchemaVersion + 1}";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => new CatalogDatabase(path));
    }
}
