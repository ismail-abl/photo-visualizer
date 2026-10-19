using PhotoApp.Core;

namespace PhotoApp.Tests;

public sealed class AppDataPathsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Windows_UsesLocalAppData()
    {
        var env = new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\x\AppData\Local" };

        var paths = AppDataPaths.Resolve(PlatformKind.Windows, k => env.GetValueOrDefault(k), @"C:\Users\x");

        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local", "PhotoManager"), paths.DataRoot);
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local", "PhotoManager", "cache"), paths.CacheRoot);
    }

    [Fact]
    public void Linux_UsesXdgWhenSet()
    {
        var env = new Dictionary<string, string> { ["XDG_DATA_HOME"] = "/data", ["XDG_CACHE_HOME"] = "/cache" };

        var paths = AppDataPaths.Resolve(PlatformKind.Linux, k => env.GetValueOrDefault(k), "/home/x");

        Assert.Equal(Path.Combine("/data", "photomanager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/cache", "photomanager"), paths.CacheRoot);
    }

    [Fact]
    public void Linux_FallsBackToHomeDefaults()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.Linux, NoEnv, "/home/x");

        Assert.Equal(Path.Combine("/home/x", ".local", "share", "photomanager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/home/x", ".cache", "photomanager"), paths.CacheRoot);
    }

    [Fact]
    public void MacOS_UsesLibraryFolders()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.MacOS, NoEnv, "/Users/x");

        Assert.Equal(Path.Combine("/Users/x", "Library", "Application Support", "PhotoManager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/Users/x", "Library", "Caches", "PhotoManager"), paths.CacheRoot);
    }

    [Fact]
    public void ManifestPathFor_IsStableAndPerOutputFolder()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.Linux, NoEnv, "/home/x");

        var a1 = paths.ManifestPathFor("/photos/a/Converted");
        var a2 = paths.ManifestPathFor("/photos/a/Converted");
        var b = paths.ManifestPathFor("/photos/b/Converted");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.StartsWith(paths.ManifestsFolder, a1);
        Assert.EndsWith(".json", a1);
    }

    [Fact]
    public void EnsureCreated_CreatesTheFolders()
    {
        using var temp = new TempDirectory();
        var paths = AppDataPaths.Resolve(
            PlatformKind.Linux,
            k => k == "XDG_DATA_HOME" ? temp.Combine("d") : k == "XDG_CACHE_HOME" ? temp.Combine("c") : null,
            "/home/x");

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.ManifestsFolder));
        Assert.True(Directory.Exists(paths.LogsFolder));
        Assert.True(Directory.Exists(paths.ThumbnailsFolder));
    }

    [Fact]
    public void ReportsFolder_IsUnderTheDataRoot()
    {
        var env = new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\x\AppData\Local" };

        var paths = AppDataPaths.Resolve(PlatformKind.Windows, k => env.GetValueOrDefault(k), @"C:\Users\x");

        Assert.Equal(Path.Combine(paths.DataRoot, "reports"), paths.ReportsFolder);
    }
}
