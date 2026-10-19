using System.Security.Cryptography;
using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class AnalyseCommandTests
{
    private static AppDataPaths PathsUnder(string root) =>
        AppDataPaths.Resolve(PlatformKind.Windows, name => name == "LOCALAPPDATA" ? root : null, root);

    // Taille, date de modification et SHA-256 de chaque fichier : la preuve que rien n'a été touché.
    private static Dictionary<string, (long, DateTime, string)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            path => path,
            path => (new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    private static string CreateLibrary(TempDirectory temp)
    {
        var photos = temp.Combine("photos");
        Directory.CreateDirectory(photos);
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestVideos.Write(Path.Combine(photos, "20260917_222944.mp4"), new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");
        return photos;
    }

    [Fact]
    public void Run_WritesTheReport_AndNeverTouchesThePhotos()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var before = Snapshot(photos);
        var report = temp.Combine("out", "rapport.csv");
        var output = new StringWriter();

        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), output, new StringWriter())
            .Run(new AnalyseRequest([photos], report, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(4, File.ReadAllLines(report).Length);   // en-tête + 3 médias
        Assert.Contains("Sûre + Probable", output.ToString());
        Assert.Contains("Rapport : " + report, output.ToString());
    }

    [Fact]
    public void Run_MalformedFile_DoesNotStopTheAnalysis()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        TestImages.WriteJpegWithExif(Path.Combine(photos, "bad.jpg"), exif =>
        {
            exif.SetValue(ImageMagick.ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ImageMagick.ExifTag.GPSTimestamp, [new ImageMagick.Rational(10, 0), new ImageMagick.Rational(0, 1), new ImageMagick.Rational(0, 1)]);
        });
        var report = temp.Combine("rapport.csv");

        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(new AnalyseRequest([photos], report, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = File.ReadAllLines(report);
        Assert.Equal(5, lines.Length);   // en-tête + 4 médias
        Assert.Contains(lines, line => line.Contains("bad.jpg") && line.Contains(Reasons.ReadFailed));
    }

    [Fact]
    public void Run_WithoutOut_WritesIntoTheReportsFolder()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var paths = PathsUnder(temp.Combine("data"));

        var exitCode = new AnalyseCommand(paths, new StringWriter(), new StringWriter(), () => new DateTime(2026, 10, 19, 10, 15, 0))
            .Run(new AnalyseRequest([photos], null, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(paths.ReportsFolder, "analyse-20261019-101500.csv")));
    }

    [Fact]
    public void Run_ExcludedFolder_IsSkipped()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        Directory.CreateDirectory(Path.Combine(photos, "skip"));
        TestImages.WritePng(Path.Combine(photos, "skip", "IMG-20210916-WA0001.png"));
        var report = temp.Combine("rapport.csv");

        new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(new AnalyseRequest([photos], report, [Path.Combine(photos, "skip")]));

        Assert.DoesNotContain("IMG-20210916-WA0001", File.ReadAllText(report));
    }

    [Fact]
    public void Run_MissingFolder_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new AnalyseCommand(PathsUnder(temp.FullPath), new StringWriter(), error)
            .Run(new AnalyseRequest([temp.Combine("absent")], null, []));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Dossier introuvable", error.ToString());
    }

    [Fact]
    public void Run_UnwritableReport_IsFatal()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var error = new StringWriter();

        // Un dossier existant à la place du fichier de rapport.
        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error)
            .Run(new AnalyseRequest([photos], photos, []));

        Assert.Equal(ExitCodes.Fatal, exitCode);
        Assert.Contains("Impossible d'écrire le rapport", error.ToString());
    }

    [Fact]
    public void ReadOnlyByConstruction_NoProjectOfTheCliReferencesOperations()
    {
        var assemblies = new[] { typeof(AnalyseCommand).Assembly, typeof(MediaAnalyzer).Assembly, typeof(MetadataReader).Assembly };

        Assert.All(assemblies, assembly =>
            Assert.DoesNotContain("PhotoApp.Operations", assembly.GetReferencedAssemblies().Select(name => name.Name)));
    }

    [Fact]
    public void ReadOnlyByConstruction_NoProjectFileReferencesOperations()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PhotoVisualizer.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        foreach (var project in new[] { "PhotoApp.Cli", "PhotoApp.Catalog", "PhotoApp.Metadata", "PhotoApp.Core" })
        {
            var references = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName, project, project + ".csproj"))
                .Descendants("ProjectReference")
                .Select(reference => (string?)reference.Attribute("Include") ?? "");
            Assert.DoesNotContain(references, include => include.Contains("PhotoApp.Operations"));
        }
    }
}
