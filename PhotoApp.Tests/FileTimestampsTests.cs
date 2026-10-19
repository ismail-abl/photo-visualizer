namespace PhotoApp.Tests;

public sealed class FileTimestampsTests
{
    [Fact]
    public void Set_ThenRead_RoundTripsCreationAndModification()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("a.jpg");
        File.WriteAllText(path, "x");
        var creation = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var write = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        FileTimestamps.Set(path, creation, write);
        var times = FileTimestamps.Read(path);

        Assert.Equal(creation, times.CreationUtc);
        Assert.Equal(write, times.LastWriteUtc);
    }
}
