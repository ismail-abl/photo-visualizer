using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class ImageScannerTests
{
    [Fact]
    public void IsSupported_ReturnsTrue_ForSupportedExtension()
    {
        var result = SupportedImageExtensions.IsSupported(".JPG");

        Assert.True(result);
    }

    [Fact]
    public void IsSupported_ReturnsFalse_ForUnsupportedExtension()
    {
        var result = SupportedImageExtensions.IsSupported(".txt");

        Assert.False(result);
    }

    [Fact]
    public void Scan_ReturnsOnlySupportedImages_FromNestedDirectories()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "photoapp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var nestedDirectory = Path.Combine(rootPath, "nested");
            Directory.CreateDirectory(nestedDirectory);

            var jpgPath = Path.Combine(rootPath, "photo1.jpg");
            var pngPath = Path.Combine(nestedDirectory, "photo2.png");
            var heicPath = Path.Combine(nestedDirectory, "photo3.heic");
            var txtPath = Path.Combine(rootPath, "notes.txt");

            File.WriteAllBytes(jpgPath, [1, 2, 3, 4]);
            File.WriteAllBytes(pngPath, [5, 6, 7]);
            File.WriteAllBytes(heicPath, [8, 9]);
            File.WriteAllText(txtPath, "not an image");

            var scanner = new ImageScanner();
            var result = scanner.Scan(rootPath);

            Assert.Equal(3, result.Count);
            Assert.Contains(result, image => string.Equals(image.FileName, "photo1.jpg", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result, image => string.Equals(image.FileName, "photo2.png", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result, image => string.Equals(image.FileName, "photo3.heic", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result, image => string.Equals(image.FileName, "notes.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Scan_SkipsExcludedFolderAndItsDescendants()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Converted", "sub"));
        File.WriteAllBytes(temp.Combine("keep.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "out.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "sub", "deep.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath, new ScanOptions([temp.Combine("Converted")]));

        Assert.Equal(["keep.jpg"], result.Select(i => i.FileName));
    }

    [Fact]
    public void Scan_SkipsDotFoldersAndZeroByteFiles()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine(".gs"));
        File.WriteAllBytes(temp.Combine(".gs", "cache.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("empty.jpg"), []);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], result.Select(i => i.FileName));
    }

    [Fact]
    public void Scan_SkipsTemporaryWorkFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine(".work-abc.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], result.Select(i => i.FileName));
    }
}
