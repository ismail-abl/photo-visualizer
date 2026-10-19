namespace PhotoApp.Tests;

public sealed class QuickTimeKeysReaderTests
{
    private static readonly DateTime Header = new(2024, 7, 11, 8, 7, 10);

    private static readonly Dictionary<string, string> AppleKeys = new()
    {
        [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0900",
        [QuickTimeKeysReader.ContentIdentifierKey] = "ID-1",
        [QuickTimeKeysReader.MakeKey] = "Apple"
    };

    [Fact]
    public void Read_ReturnsTheAppleKeysAsWritten()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys);

        var keys = QuickTimeKeysReader.Read(path);

        Assert.Equal("2024-07-11T17:07:10+0900", keys[QuickTimeKeysReader.CreationDateKey]);
        Assert.Equal("ID-1", keys[QuickTimeKeysReader.ContentIdentifierKey]);
        Assert.Equal("Apple", keys[QuickTimeKeysReader.MakeKey]);
    }

    [Fact]
    public void Read_IsoMetaFullBox_IsSupported()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mp4");
        TestVideos.Write(path, Header, AppleKeys, brand: "isom", isoMeta: true);

        Assert.Equal("ID-1", QuickTimeKeysReader.Read(path)[QuickTimeKeysReader.ContentIdentifierKey]);
    }

    [Fact]
    public void Read_FindsMoovAfterTheMediaData()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys, mediaDataBytes: 4096);

        Assert.Equal("Apple", QuickTimeKeysReader.Read(path)[QuickTimeKeysReader.MakeKey]);
    }

    [Fact]
    public void Read_WithoutAppleKeys_IsEmpty()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mp4");
        TestVideos.Write(path, Header, brand: "isom");

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_NotAQuickTimeFile_IsEmpty()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_TruncatedFile_IsEmpty_WithoutThrowing()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - 20)]);

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_MissingFile_IsEmpty()
    {
        Assert.Empty(QuickTimeKeysReader.Read(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid().ToString("N") + ".mov")));
    }
}
