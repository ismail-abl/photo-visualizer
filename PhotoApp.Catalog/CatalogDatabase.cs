using Microsoft.Data.Sqlite;

namespace PhotoApp.Catalog;

/// <summary>
/// La base SQLite de l'application (<c>catalog.db</c>, spec de phase 2, §6), en mode WAL. Une connexion par opération,
/// sans pool : aucune poignée ne reste ouverte sur le fichier. Le dossier de la base doit exister (<c>AppDataPaths.EnsureCreated</c>).
/// </summary>
public sealed class CatalogDatabase
{
    public const int SchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

        CREATE TABLE IF NOT EXISTS batch (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_utc TEXT NOT NULL,
            folders TEXT NOT NULL,
            state TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS batch_item (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            batch_id INTEGER NOT NULL REFERENCES batch(id),
            rank INTEGER NOT NULL,
            group_id INTEGER NOT NULL,
            role TEXT NOT NULL,
            source_path TEXT NOT NULL,
            target_path TEXT NOT NULL,
            state TEXT NOT NULL,
            error TEXT NULL,
            decided_date TEXT NOT NULL,
            source_size INTEGER NOT NULL,
            source_last_write_utc TEXT NOT NULL,
            source_creation_utc TEXT NOT NULL,
            done_size INTEGER NULL,
            done_last_write_utc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_batch_item_batch ON batch_item(batch_id, rank);

        CREATE TABLE IF NOT EXISTS quarantine_entry (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_utc TEXT NOT NULL,
            original_path TEXT NOT NULL,
            stored_path TEXT NOT NULL,
            batch_id INTEGER NULL,
            state TEXT NOT NULL
        );
        """;

    private readonly string _connectionString;

    public CatalogDatabase(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(DatabasePath)!;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Le dossier de la base de données n'existe pas : {directory}");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;");
        return connection;
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void Initialize()
    {
        using var connection = Open();
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, Schema);

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version";
        var existing = read.ExecuteScalar();
        if (existing is null)
        {
            Execute(connection, $"INSERT INTO schema_version (version) VALUES ({SchemaVersion})");
        }
        else if (Convert.ToInt32(existing) > SchemaVersion)
        {
            throw new InvalidOperationException("La base de données a été créée par une version plus récente de PhotoManager.");
        }
    }
}
