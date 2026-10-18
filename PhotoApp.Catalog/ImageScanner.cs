using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Catalog;

public sealed class ImageScanner
{
    public IReadOnlyList<ImageFileInfo> Scan(string rootPath, ScanOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(rootPath));
        }

        var images = new List<ImageFileInfo>();

        if (!Directory.Exists(rootPath))
        {
            return images;
        }

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var excluded = new HashSet<string>(
            (options?.ExcludedFolders ?? []).Select(NormalizeFolder),
            comparer);

        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootPath);

        while (directoriesToScan.Count > 0)
        {
            var currentDirectory = directoriesToScan.Pop();

            foreach (var subDirectory in EnumerateDirectoriesSafe(currentDirectory))
            {
                if (excluded.Contains(NormalizeFolder(subDirectory)) || IsHidden(subDirectory))
                {
                    continue;
                }

                directoriesToScan.Push(subDirectory);
            }

            foreach (var filePath in EnumerateFilesSafe(currentDirectory))
            {
                var extension = Path.GetExtension(filePath);
                if (!SupportedImageExtensions.IsSupported(extension) || IsHidden(filePath))
                {
                    continue;
                }

                var imageInfo = CreateImageFileInfo(filePath);
                if (imageInfo is { FileSizeBytes: > 0 })
                {
                    images.Add(imageInfo);
                }
            }
        }

        return images;
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

    private static IEnumerable<string> EnumerateDirectoriesSafe(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(directoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static ImageFileInfo? CreateImageFileInfo(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return new ImageFileInfo(
                fileInfo.FullName,
                fileInfo.Name,
                fileInfo.Extension,
                fileInfo.Length);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
