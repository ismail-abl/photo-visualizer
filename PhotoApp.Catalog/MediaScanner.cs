using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Catalog;

/// <summary>
/// Parcourt un dossier récursivement : photos, vidéos et leurs sidecars (.xmp, .aae) du même nom de base.
/// Écarte les dossiers exclus, les fichiers et dossiers cachés et les fichiers vides, et les compte.
/// </summary>
public sealed class MediaScanner
{
    public ScanResult Scan(string rootPath, ScanOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(rootPath));
        }

        var media = new List<ScannedMedia>();
        if (!Directory.Exists(rootPath))
        {
            return new ScanResult(media, ScanStatistics.None);
        }

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var excluded = new HashSet<string>((options?.ExcludedFolders ?? []).Select(NormalizeFolder), comparer);
        int hiddenFiles = 0, hiddenFolders = 0, emptyFiles = 0, inaccessibleFolders = 0;

        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootPath);

        while (directoriesToScan.Count > 0)
        {
            var currentDirectory = directoriesToScan.Pop();
            var subDirectories = ListSafe(() => Directory.EnumerateDirectories(currentDirectory));
            var files = ListSafe(() => Directory.EnumerateFiles(currentDirectory));
            if (subDirectories is null || files is null)
            {
                inaccessibleFolders++;
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                if (excluded.Contains(NormalizeFolder(subDirectory)))
                {
                    continue;
                }

                if (IsHidden(subDirectory))
                {
                    hiddenFolders++;
                    continue;
                }

                directoriesToScan.Push(subDirectory);
            }

            var sidecars = files.Where(file => SupportedMediaExtensions.IsSidecar(Path.GetExtension(file)) && !IsHidden(file)).ToList();
            foreach (var file in files)
            {
                if (!SupportedMediaExtensions.IsSupported(Path.GetExtension(file)))
                {
                    continue;
                }

                if (IsHidden(file))
                {
                    hiddenFiles++;
                    continue;
                }

                var info = TryGetInfo(file);
                if (info is null)
                {
                    continue;
                }

                if (info.Length == 0)
                {
                    emptyFiles++;
                    continue;
                }

                media.Add(new ScannedMedia(info.FullName, info.Length, info.LastWriteTimeUtc, SidecarsOf(info.FullName, sidecars)));
            }
        }

        return new ScanResult(media, new ScanStatistics(hiddenFiles, hiddenFolders, emptyFiles, inaccessibleFolders));
    }

    // photo.xmp ou photo.jpg.xmp (Lightroom, darktable) ; IMG_0477.AAE vaut pour la photo et la vidéo d'une Live Photo.
    private static IReadOnlyList<string> SidecarsOf(string mediaPath, List<string> sidecars)
    {
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        var name = Path.GetFileName(mediaPath);
        return sidecars
            .Where(sidecar =>
            {
                var sidecarStem = Path.GetFileNameWithoutExtension(sidecar);
                return string.Equals(sidecarStem, stem, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(sidecarStem, name, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static string NormalizeFolder(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Dossiers et fichiers cachés : caches d'applications (.gs), fichiers de travail (.work-…), corbeilles système.
    private static bool IsHidden(string path)
    {
        if (Path.GetFileName(path).StartsWith('.'))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // L'énumération est matérialisée ici : une erreur d'accès survient pendant le parcours, pas à l'appel.
    private static List<string>? ListSafe(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }

    private static FileInfo? TryGetInfo(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            _ = info.Length;   // force la lecture : une erreur d'accès est levée ici
            return info;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }
}
