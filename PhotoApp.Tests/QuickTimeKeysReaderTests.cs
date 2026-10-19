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

    [Fact]
    public void Read_HugeLargeSize_IsEmpty_WithoutThrowing()
    {
        // moov de premier niveau dont la taille 64 bits est démesurée.
        using var top = new MemoryStream();
        top.Write([0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);
        top.Write([0, 0, 0, 1, (byte)'m', (byte)'o', (byte)'o', (byte)'v', 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0]);
        top.Position = 0;
        Assert.Empty(QuickTimeKeysReader.Read(top));

        // moov valide, mais son « keys » interne a une taille 64 bits démesurée (position + taille déborde).
        using var inner = new MemoryStream();
        inner.Write([0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);
        inner.Write([0, 0, 0, 40, (byte)'m', (byte)'o', (byte)'o', (byte)'v']);
        inner.Write([0, 0, 0, 32, (byte)'m', (byte)'e', (byte)'t', (byte)'a', 0, 0, 0, 8, (byte)'i', (byte)'l', (byte)'s', (byte)'t']);
        inner.Write([0, 0, 0, 1, (byte)'k', (byte)'e', (byte)'y', (byte)'s', 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0]);
        inner.Position = 0;
        Assert.Empty(QuickTimeKeysReader.Read(inner));
    }

    [Fact]
    public void Read_BogusKeySize_IsEmpty_WithoutThrowing()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys);
        var bytes = File.ReadAllBytes(path);
        var type = bytes.AsSpan().IndexOf("keys"u8);
        Assert.True(type > 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(type + 12), 0x7FFFFFF0);
        File.WriteAllBytes(path, bytes);

        var keys = QuickTimeKeysReader.Read(path);

        Assert.Empty(keys);
    }
}
