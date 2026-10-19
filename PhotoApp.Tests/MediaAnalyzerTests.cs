using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class MediaAnalyzerTests
{
    private sealed class Collector : IProgress<AnalysisProgress>
    {
        private readonly object _gate = new();

        public List<AnalysisProgress> Reports { get; } = [];

        public void Report(AnalysisProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }

    private static readonly NormalizationOptions Options = new(new DateTime(2026, 10, 19, 12, 0, 0), TestZones.PlusOne);

    [Fact]
    public void Analyze_DecidesEveryMedia_AndCountsWhatWasSkipped()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        Directory.CreateDirectory(Path.Combine(root, ".cache"));
        Directory.CreateDirectory(Path.Combine(root, "Converted"));
        TestImages.WritePng(Path.Combine(root, "IMG-20210915-WA0005.png"));
        TestImages.WritePng(Path.Combine(root, "Screenshot_20240410_111652.png"));
        TestImages.WriteJpeg(Path.Combine(root, "holiday.jpg"), "2023:03:04 13:20:15");
        TestVideos.Write(Path.Combine(root, "IMG_0477.MOV"), new DateTime(2024, 7, 11, 15, 7, 10), new Dictionary<string, string>
        {
            [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0200",
            [QuickTimeKeysReader.MakeKey] = "Apple"
        });
        TestImages.WritePng(Path.Combine(root, ".cache", "thumb.png"));
        TestImages.WritePng(Path.Combine(root, "Converted", "IMG-20210915-000000-01.jpg"));
        File.WriteAllBytes(Path.Combine(root, "empty.jpg"), []);
        var progress = new Collector();

        var analysis = new MediaAnalyzer().Analyze([root], [Path.Combine(root, "Converted")], progress, Options);

        var byName = analysis.Results.ToDictionary(r => r.Facts.File.FileName);
        Assert.Equal(4, byName.Count);
        Assert.Equal(Reasons.MessagingName, byName["IMG-20210915-WA0005.png"].Decision.Reason);
        Assert.Equal(Reasons.ScreenshotName, byName["Screenshot_20240410_111652.png"].Decision.Reason);
        Assert.Equal(Reasons.ExifOnly, byName["holiday.jpg"].Decision.Reason);
        Assert.Equal(Reasons.QuickTimeApple, byName["IMG_0477.MOV"].Decision.Reason);
        Assert.Equal(MediaKind.Video, byName["IMG_0477.MOV"].Kind);
        Assert.Equal("VID-20240711-170710.mov", byName["IMG_0477.MOV"].ProposedName);
        Assert.Equal(1, analysis.Statistics.HiddenFolders);
        Assert.Equal(1, analysis.Statistics.EmptyFiles);
        Assert.Equal(4, progress.Reports.Count);
        Assert.Contains(new AnalysisProgress(4, 4), progress.Reports);
    }

    [Fact]
    public void Analyze_OverlappingRoots_DoNotDuplicateFiles()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("sub"));
        TestImages.WritePng(temp.Combine("sub", "IMG_20230304_132015.png"));

        var analysis = new MediaAnalyzer().Analyze([temp.FullPath, temp.Combine("sub")], [], options: Options);

        Assert.Single(analysis.Results);
    }

    [Fact]
    public void Analyze_MalformedFile_DoesNotStopTheAnalysis()
    {
        using var temp = new TempDirectory();
        TestImages.WriteJpeg(temp.Combine("holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WriteJpegWithExif(temp.Combine("bad.jpg"), exif =>
        {
            exif.SetValue(ImageMagick.ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ImageMagick.ExifTag.GPSTimestamp, [new ImageMagick.Rational(10, 0), new ImageMagick.Rational(0, 1), new ImageMagick.Rational(0, 1)]);
        });

        var analysis = new MediaAnalyzer().Analyze([temp.FullPath], [], options: Options);

        Assert.Equal(2, analysis.Results.Count);
        Assert.Equal(Reasons.ReadFailed, analysis.Results.Single(r => r.Facts.File.FileName == "bad.jpg").Decision.Reason);
    }
}
