using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class ConversionServiceTests
{
    private static IReadOnlyList<ImageFileInfo> ScanAndResolve(string root, string outputFolder)
    {
        var resolver = new ImageDateResolver();
        return new ImageScanner()
            .Scan(root, new ScanOptions([outputFolder]))
            .Select(i => new ImageFileInfo(i.FullPath, i.FileName, i.Extension, i.FileSizeBytes, resolver.Resolve(i.FullPath)))
            .ToList();
    }

    private static string[] OutputNames(string outputFolder) =>
        Directory.GetFiles(outputFolder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void SecondPassOnTheSameFolder_ProducesNoErrorAndNoDuplicate()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);

        TestImages.WritePng(Path.Combine(root, "Screenshot_20240410_111652.png"));
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));                 // même seconde
        TestImages.WriteJpeg(Path.Combine(root, "holiday.jpg"), "2023:03:04 13:20:15");     // date EXIF
        TestImages.WritePng(Path.Combine(root, "no-date-at-all.png"));                       // date non fiable

        var service = new ConversionService();

        var first = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        var filesAfterFirst = OutputNames(output);

        var second = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        var filesAfterSecond = OutputNames(output);

        Assert.Equal(3, first.Converted);
        Assert.Equal(0, first.Failed);
        Assert.Equal(1, first.SkippedUnreliableDate);
        Assert.Equal(0, first.SkippedUnsupportedFormat);
        Assert.Equal(
            ["IMG-20230304-132015.jpg", "IMG-20240410-111652-01.jpg", "IMG-20240410-111652.jpg"],
            filesAfterFirst);

        Assert.Equal(0, second.Converted);
        Assert.Equal(3, second.AlreadyDone);
        Assert.Equal(0, second.Failed);
        Assert.Equal(filesAfterFirst, filesAfterSecond);
    }

    [Fact]
    public void DeletingAnOutputFile_MakesItBeConvertedAgainUnderTheSameName()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));
        var service = new ConversionService();
        service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        File.Delete(Path.Combine(output, "IMG-20240410-111652.jpg"));

        var again = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);

        Assert.Equal(1, again.Converted);
        Assert.True(File.Exists(Path.Combine(output, "IMG-20240410-111652.jpg")));
    }

    [Fact]
    public void CorruptFile_FailsAlone_AndLeavesNoOrphan()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "IMG_20240101_101010.png"), [1, 2, 3]);   // pas une vraie image
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));

        var summary = new ConversionService().Run(ScanAndResolve(root, output), output, manifestPath, 90, false);

        Assert.Equal(1, summary.Converted);
        Assert.Equal(1, summary.Failed);
        Assert.NotNull(summary.FirstError);
        Assert.Equal(["IMG-20240410-111652.jpg"], OutputNames(output));
    }

    [Fact]
    public void ToDisplayText_SummarisesEveryCounter()
    {
        var text = new ConversionSummary(3, 2, 1, 4, 1, "boom").ToDisplayText();

        Assert.Contains("3 converti", text);
        Assert.Contains("2 déjà", text);
        Assert.Contains("1 ignoré(s) (date non fiable)", text);
        Assert.Contains("4 ignoré(s) (format non pris en charge)", text);
        Assert.Contains("1 échec", text);
        Assert.Contains("boom", text);
    }
}
