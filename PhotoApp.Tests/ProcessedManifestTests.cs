using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class ProcessedManifestTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("sub", "m.json");
        var manifest = ProcessedManifest.Empty();
        manifest.Record(new ManifestEntry("/p/a.png", 10, 20, "IMG-20240105-150412.jpg"));

        manifest.Save(path);
        var loaded = ProcessedManifest.Load(path);

        Assert.Equal(1, loaded.Count);
        Assert.Equal(new PreviousOutput(10, 20, "IMG-20240105-150412.jpg"), loaded.ToPrevious()["/p/a.png"]);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        using var temp = new TempDirectory();

        Assert.Equal(0, ProcessedManifest.Load(temp.Combine("none.json")).Count);
    }

    [Fact]
    public void Load_CorruptFile_Throws_InsteadOfSilentlyReprocessingEverything()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("m.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => ProcessedManifest.Load(path));
    }

    [Fact]
    public void PruneMissingTargets_DropsEntriesWhoseOutputFileIsGone()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("kept.jpg"), [1]);
        var manifest = ProcessedManifest.Empty();
        manifest.Record(new ManifestEntry("/p/a.png", 1, 1, "kept.jpg"));
        manifest.Record(new ManifestEntry("/p/b.png", 1, 1, "gone.jpg"));

        manifest.PruneMissingTargets(temp.FullPath);

        Assert.Equal(["/p/a.png"], manifest.ToPrevious().Keys);
    }
}
