namespace PhotoApp.Tests;

public sealed class MediaScannerTests
{
    private static string[] Names(ScanResult result) =>
        result.Media.Select(m => m.FileName).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Scan_ReturnsPhotosAndVideos_FromNestedDirectories()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("nested"));
        File.WriteAllBytes(temp.Combine("photo1.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "photo2.png"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "photo3.heic"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "clip.MOV"), [1]);
        File.WriteAllBytes(temp.Combine("video.mp4"), [1]);
        File.WriteAllText(temp.Combine("notes.txt"), "pas un média");

        var result = new MediaScanner().Scan(temp.FullPath);

        Assert.Equal(["clip.MOV", "photo1.jpg", "photo2.png", "photo3.heic", "video.mp4"], Names(result));
    }

    [Fact]
    public void Scan_RecordsSizeAndUtcModificationDate()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        File.WriteAllBytes(path, [1, 2, 3]);
        var written = new DateTime(2023, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var media = new MediaScanner().Scan(temp.FullPath).Media.Single();

        Assert.Equal(3, media.SizeBytes);
        Assert.Equal(written, media.LastWriteUtc);
    }

    [Fact]
    public void Scan_AttachesSidecarsWithTheSameBaseName()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("IMG_0477.HEIC"), [1]);
        File.WriteAllBytes(temp.Combine("IMG_0477.MOV"), [1]);
        File.WriteAllText(temp.Combine("IMG_0477.AAE"), "aae");
        File.WriteAllBytes(temp.Combine("scan.jpg"), [1]);
        File.WriteAllText(temp.Combine("scan.jpg.xmp"), "xmp");
        File.WriteAllText(temp.Combine("orphan.xmp"), "xmp");

        var media = new MediaScanner().Scan(temp.FullPath).Media.ToDictionary(m => m.FileName);

        Assert.Equal(["IMG_0477.AAE"], media["IMG_0477.HEIC"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(["IMG_0477.AAE"], media["IMG_0477.MOV"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(["scan.jpg.xmp"], media["scan.jpg"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(3, media.Count);
    }

    [Fact]
    public void Scan_SkipsExcludedFolderAndItsDescendants()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Converted", "sub"));
        File.WriteAllBytes(temp.Combine("keep.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "out.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "sub", "deep.jpg"), [1]);

        var result = new MediaScanner().Scan(temp.FullPath, new ScanOptions([temp.Combine("Converted")]));

        Assert.Equal(["keep.jpg"], Names(result));
    }

    [Fact]
    public void Scan_CountsHiddenAndEmptyEntries()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine(".gs"));
        File.WriteAllBytes(temp.Combine(".gs", "cache.jpg"), [1]);
        File.WriteAllBytes(temp.Combine(".work-abc.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("empty.jpg"), []);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new MediaScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], Names(result));
        Assert.Equal(new ScanStatistics(HiddenFiles: 1, HiddenFolders: 1, EmptyFiles: 1, InaccessibleFolders: 0), result.Statistics);
    }

    [Fact]
    public void Scan_MissingRoot_IsEmpty()
    {
        var result = new MediaScanner().Scan(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid().ToString("N")));

        Assert.Empty(result.Media);
    }

    [Fact]
    public void ScanStatistics_Add()
    {
        Assert.Equal(new ScanStatistics(2, 4, 6, 8), new ScanStatistics(1, 2, 3, 4).Add(new ScanStatistics(1, 2, 3, 4)));
    }
}
