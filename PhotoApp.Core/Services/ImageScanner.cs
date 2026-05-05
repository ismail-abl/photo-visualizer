using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public sealed class ImageScanner
{
    public IReadOnlyList<ImageFileInfo> Scan(string rootPath)
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

        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootPath);

        while (directoriesToScan.Count > 0)
        {
            var currentDirectory = directoriesToScan.Pop();

            foreach (var subDirectory in EnumerateDirectoriesSafe(currentDirectory))
            {
                directoriesToScan.Push(subDirectory);
            }

            foreach (var filePath in EnumerateFilesSafe(currentDirectory))
            {
                var extension = Path.GetExtension(filePath);
                if (!SupportedImageExtensions.IsSupported(extension))
                {
                    continue;
                }

                var imageInfo = CreateImageFileInfo(filePath);
                if (imageInfo is not null)
                {
                    images.Add(imageInfo);
                }
            }
        }

        return images;
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
