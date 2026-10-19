using System.Security.Cryptography;
using System.Text;

namespace PhotoApp.Core;

public enum PlatformKind
{
    Windows,
    Linux,
    MacOS
}

/// <summary>Point unique de résolution des dossiers de l'application (spec §9).</summary>
public sealed class AppDataPaths
{
    private AppDataPaths(string dataRoot, string cacheRoot)
    {
        DataRoot = dataRoot;
        CacheRoot = cacheRoot;
    }

    public string DataRoot { get; }

    public string CacheRoot { get; }

    public string ManifestsFolder => Path.Combine(DataRoot, "manifests");

    public string LogsFolder => Path.Combine(DataRoot, "logs");

    public string ReportsFolder => Path.Combine(DataRoot, "reports");

    public string DatabasePath => Path.Combine(DataRoot, "catalog.db");

    public string ThumbnailsFolder => Path.Combine(CacheRoot, "thumbnails");

    public static AppDataPaths ForCurrentUser()
    {
        var platform = OperatingSystem.IsWindows() ? PlatformKind.Windows
            : OperatingSystem.IsMacOS() ? PlatformKind.MacOS
            : PlatformKind.Linux;

        return Resolve(
            platform,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public static AppDataPaths Resolve(PlatformKind platform, Func<string, string?> getEnv, string home)
    {
        switch (platform)
        {
            case PlatformKind.Windows:
            {
                var local = getEnv("LOCALAPPDATA");
                if (string.IsNullOrWhiteSpace(local))
                {
                    local = Path.Combine(home, "AppData", "Local");
                }

                var root = Path.Combine(local, "PhotoManager");
                return new AppDataPaths(root, Path.Combine(root, "cache"));
            }
            case PlatformKind.MacOS:
                return new AppDataPaths(
                    Path.Combine(home, "Library", "Application Support", "PhotoManager"),
                    Path.Combine(home, "Library", "Caches", "PhotoManager"));
            default:
            {
                var data = getEnv("XDG_DATA_HOME");
                var cache = getEnv("XDG_CACHE_HOME");
                return new AppDataPaths(
                    Path.Combine(string.IsNullOrWhiteSpace(data) ? Path.Combine(home, ".local", "share") : data, "photomanager"),
                    Path.Combine(string.IsNullOrWhiteSpace(cache) ? Path.Combine(home, ".cache") : cache, "photomanager"));
            }
        }
    }

    public string ManifestPathFor(string outputFolder)
    {
        var normalized = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Path.Combine(ManifestsFolder, Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + ".json");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ManifestsFolder);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(ThumbnailsFolder);
    }
}
