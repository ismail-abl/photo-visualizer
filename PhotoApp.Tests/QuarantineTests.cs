using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class QuarantineTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static (Quarantine Quarantine, SqliteQuarantineLog Log, string Root, string Photos) Setup(TempDirectory temp, Func<DateTime>? clock = null)
    {
        var root = temp.Combine("quarantine");
        Directory.CreateDirectory(root);
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        return (new Quarantine(root, log, clock ?? (() => Now)), log, root, Photos(temp));
    }

    [Fact]
    public void Hold_MovesTheFileUnderTheRoot_AndRecordsWhereItCameFrom()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, root, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");

        var entry = quarantine.Hold(path, batchId: 3);

        Assert.False(File.Exists(path));
        Assert.StartsWith(root, entry.StoredPath);
        Assert.Equal("A", File.ReadAllText(entry.StoredPath));
        Assert.Equal(path, entry.OriginalPath);
        Assert.Equal(3, entry.BatchId);
        Assert.Equal(QuarantineState.Held, entry.State);
        Assert.Equal(entry, log.Get(entry.Id));
    }

    [Fact]
    public void Hold_TwoFilesWithTheSameName_DoNotCollide()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);
        var first = quarantine.Hold(WriteFile(photos, "a.jpg", "1"));
        var second = quarantine.Hold(WriteFile(photos, "a.jpg", "2"));

        Assert.NotEqual(first.StoredPath, second.StoredPath);
        Assert.Equal("1", File.ReadAllText(first.StoredPath));
        Assert.Equal("2", File.ReadAllText(second.StoredPath));
    }

    [Fact]
    public void Hold_MissingRoot_Throws()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        var quarantine = new Quarantine(temp.Combine("absent"), log);
        var path = WriteFile(Photos(temp), "a.jpg");

        Assert.Throws<DirectoryNotFoundException>(() => quarantine.Hold(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Hold_WhenTheLogFails_PutsTheFileBack_AndRemovesItsFolder_AndRethrows()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("quarantine");
        Directory.CreateDirectory(root);
        var log = new ThrowingQuarantineLog();
        var quarantine = new Quarantine(root, log, () => Now);
        var path = WriteFile(Photos(temp), "a.jpg", "A");

        var error = Assert.Throws<InvalidOperationException>(() => quarantine.Hold(path, batchId: 3));

        Assert.Equal("base verrouillée", error.Message);
        Assert.Equal(1, log.AddCalls);
        Assert.Equal("A", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(root).SelectMany(Directory.GetDirectories));   // plus de dossier <date>\<guid>
        Assert.Empty(log.List());
    }

    [Fact]
    public void Hold_WhenTheLogFails_AndTheFileCannotBePutBack_ThrowsAnIOExceptionNamingTheStoredPath()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("quarantine");
        Directory.CreateDirectory(root);
        var quarantine = new Quarantine(root, new ThrowingQuarantineLog(), () => Now);
        var path = WriteFile(Photos(temp), "a.jpg", "A");
        var moves = 0;

        var error = Assert.Throws<IOException>(() => quarantine.Hold(path, null, (from, to) =>
        {
            if (++moves > 1)
            {
                throw new IOException("remise en place impossible");   // le déplacement aller a réussi, pas le retour
            }

            File.Move(from, to, overwrite: false);
        }));

        var stored = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Single();
        Assert.Contains(stored, error.Message);
        Assert.Contains("remise en place impossible", error.Message);
        var original = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal("base verrouillée", original.Message);
        Assert.Equal("A", File.ReadAllText(stored));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Hold_MissingFile_Throws()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);

        Assert.Throws<FileNotFoundException>(() => quarantine.Hold(Path.Combine(photos, "absent.jpg")));
    }

    [Fact]
    public void Restore_PutsTheFileBack_AndMarksTheEntry()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");
        var entry = quarantine.Hold(path);

        quarantine.Restore(entry.Id);

        Assert.Equal("A", File.ReadAllText(path));
        Assert.False(File.Exists(entry.StoredPath));
        Assert.Equal(QuarantineState.Restored, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void Restore_WhenTheOriginalNameIsTaken_Throws_AndKeepsBothFiles()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");
        var entry = quarantine.Hold(path);
        WriteFile(photos, "a.jpg", "autre");

        Assert.Throws<IOException>(() => quarantine.Restore(entry.Id));

        Assert.Equal("autre", File.ReadAllText(path));
        Assert.Equal("A", File.ReadAllText(entry.StoredPath));
        Assert.Equal(QuarantineState.Held, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void Restore_AnEntryThatIsNotHeld_Throws()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);
        var entry = quarantine.Hold(WriteFile(photos, "a.jpg"));
        quarantine.Restore(entry.Id);

        Assert.Throws<InvalidOperationException>(() => quarantine.Restore(entry.Id));
    }

    [Fact]
    public void Purge_DeletesTheStoredFile_AndMarksTheEntry()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var entry = quarantine.Hold(WriteFile(photos, "a.jpg"));

        quarantine.Purge(entry.Id);

        Assert.False(File.Exists(entry.StoredPath));
        Assert.Equal(QuarantineState.Purged, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void PurgeOlderThan_OnlyPurgesHeldEntriesPastTheAge()
    {
        using var temp = new TempDirectory();
        var clock = Now;
        var (quarantine, log, _, photos) = Setup(temp, () => clock);
        var old = quarantine.Hold(WriteFile(photos, "old.jpg"));
        clock = Now.AddDays(20);
        var recent = quarantine.Hold(WriteFile(photos, "recent.jpg"));
        clock = Now.AddDays(31);

        var purged = quarantine.PurgeOlderThan(TimeSpan.FromDays(30));

        Assert.Equal(1, purged);
        Assert.Equal(QuarantineState.Purged, log.Get(old.Id)!.State);
        Assert.Equal(QuarantineState.Held, log.Get(recent.Id)!.State);
        Assert.True(File.Exists(recent.StoredPath));
    }

    /// <summary>Un journal dont l'enregistrement échoue toujours : sert à vérifier que <c>Hold</c> défait son déplacement.</summary>
    private sealed class ThrowingQuarantineLog : IQuarantineLog
    {
        public int AddCalls { get; private set; }

        public long Add(DateTime createdUtc, string originalPath, string storedPath, long? batchId)
        {
            AddCalls++;
            throw new InvalidOperationException("base verrouillée");
        }

        public QuarantineEntry? Get(long id) => null;

        public IReadOnlyList<QuarantineEntry> List(QuarantineState? state = null) => [];

        public void SetState(long id, QuarantineState state)
        {
        }
    }
}
