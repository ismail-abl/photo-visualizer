using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SqliteQuarantineLogTests
{
    private static readonly DateTime Created = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_ThenGet_RoundTripsTheEntry()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));

        var id = log.Add(Created, @"C:\photos\a.jpg", @"D:\q\a.jpg", batchId: 7);

        Assert.Equal(new QuarantineEntry(id, Created, @"C:\photos\a.jpg", @"D:\q\a.jpg", 7, QuarantineState.Held), log.Get(id));
    }

    [Fact]
    public void Get_Unknown_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db"))).Get(1));
    }

    [Fact]
    public void SetState_AndListByState()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        var first = log.Add(Created, "a", "qa", null);
        var second = log.Add(Created, "b", "qb", null);

        log.SetState(first, QuarantineState.Restored);

        Assert.Equal([second], log.List(QuarantineState.Held).Select(entry => entry.Id));
        Assert.Equal([first, second], log.List().Select(entry => entry.Id));
        Assert.Null(log.Get(second)!.BatchId);
    }
}
