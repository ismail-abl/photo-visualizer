using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class NormalizationEngineTests
{
    private sealed class PathHasher : IContentHasher
    {
        public string Hash(string fullPath) => fullPath;
    }

    private static readonly DateTime CopiedOn = new(2025, 6, 1, 10, 0, 0);

    internal static NormalizationEngine Engine() =>
        new(new NormalizationOptions(new DateTime(2026, 10, 19, 12, 0, 0), TestZones.PlusOne), new PathHasher());

    internal static MediaFacts Facts(string path, RawMetadata? metadata = null) =>
        new(new ScannedMedia(path, 100, CopiedOn, []), metadata ?? RawMetadata.Empty);

    [Fact]
    public void WhatsAppDay_IsNumberedByTheCounter_RegardlessOfInputOrder()
    {
        var batch = new[]
        {
            Facts("/p/IMG-20210915-WA0010.jpg"),
            Facts("/p/IMG-20210915-WA0002.jpg"),
            Facts("/p/IMG-20210915-WA0005.jpg")
        };

        var forward = Engine().Normalize(batch);
        var backward = Engine().Normalize(batch.Reverse().ToList());

        Assert.Equal(
            ["IMG-20210915-000000-01.jpg", "IMG-20210915-000000-02.jpg", "IMG-20210915-000000-03.jpg"],
            forward.Select(r => r.ProposedName));
        Assert.Equal(["/p/IMG-20210915-WA0002.jpg", "/p/IMG-20210915-WA0005.jpg", "/p/IMG-20210915-WA0010.jpg"], forward.Select(r => r.Facts.File.FullPath));
        Assert.Equal(forward.Select(r => r.ProposedName), backward.Select(r => r.ProposedName));
        Assert.All(forward, r => Assert.Equal("whatsapp", r.Profile?.Id));
    }

    [Fact]
    public void AlreadyRenamedFiles_KeepTheirName_AndNewcomersAreNumberedAfterThem()
    {
        var results = Engine().Normalize(
        [
            Facts("/p/IMG-20210915-000000-01.jpg"),
            Facts("/p/IMG-20210915-000000-02.jpg"),
            Facts("/p/IMG-20210915-WA0007.jpg")
        ]);

        Assert.Equal(
            ["IMG-20210915-000000-01.jpg", "IMG-20210915-000000-02.jpg", "IMG-20210915-000000-03.jpg"],
            results.OrderBy(r => r.Facts.File.FullPath, StringComparer.Ordinal).Select(r => r.ProposedName));
    }

    [Fact]
    public void LivePhoto_IsFlaggedOnBothHalves_AndTheVideoKeepsTheImgPrefix()
    {
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));

        var results = Engine().Normalize(
        [
            Facts("/p/IMG_0477.HEIC", new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }),
            Facts("/p/IMG_0477.MOV", new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" })
        ]);

        Assert.Equal(["IMG-20240711-170710.heic", "IMG-20240711-170710.mov"], results.Select(r => r.ProposedName));
        Assert.All(results, r => Assert.True(r.Decision.Flags.HasFlag(DecisionFlags.LivePhoto)));
        Assert.Equal([MediaKind.Photo, MediaKind.Video], results.Select(r => r.Kind));
    }

    [Fact]
    public void RealFormat_DifferentFromTheExtension_IsFlagged_AndUsedForTheName()
    {
        var exif = new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(1));

        var result = Engine().Normalize([Facts("/p/photo.jpg", new RawMetadata { Dates = [exif], DetectedExtension = "png" })]).Single();

        Assert.Equal(".png", result.TargetExtension);
        Assert.True(result.Decision.Flags.HasFlag(DecisionFlags.RealFormat));
        Assert.Equal("IMG-20240105-150412.png", result.ProposedName);
    }

    [Theory]
    [InlineData("/p/photo.heif", "heic", ".heif")]
    [InlineData("/p/photo.JPEG", "jpg", ".jpg")]
    [InlineData("/p/photo.tif", "tiff", ".tif")]
    public void SameFormatUnderAnotherExtension_IsNotFlagged(string path, string detected, string target)
    {
        var result = Engine().Normalize([Facts(path, new RawMetadata { DetectedExtension = detected })]).Single();

        Assert.Equal(target, result.TargetExtension);
        Assert.False(result.Decision.Flags.HasFlag(DecisionFlags.RealFormat));
    }

    [Fact]
    public void AmbiguousNames_AreSettledAcrossTheBatch()
    {
        var results = Engine().Normalize([Facts("/p/25-12-2024 10-00-00.jpg"), Facts("/p/03-04-2025 10-00-00.jpg")]);

        var settled = results.Single(r => r.Facts.File.FileName.StartsWith("03"));
        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), settled.Decision.Date?.WallClock);
        Assert.Equal(Reasons.GenericName, settled.Decision.Reason);
    }

    [Fact]
    public void FileDate_CanSettleAnAmbiguousName()
    {
        // Date de modification 2025-03-04 09:00 UTC = 10:00 à UTC+1 : la lecture MM-JJ l'emporte.
        var facts = new MediaFacts(new ScannedMedia("/p/03-04-2025 10-00-00.jpg", 100, new DateTime(2025, 3, 4, 9, 0, 0), []), RawMetadata.Empty);

        var result = Engine().Normalize([facts]).Single();

        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), result.Decision.Date?.WallClock);
    }

    [Fact]
    public void Video_GetsTheVidPrefix()
    {
        var header = new DateCandidate(new DateTime(2026, 9, 17, 20, 29, 44), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        var result = Engine().Normalize([Facts("/p/20260917_222944.mp4", new RawMetadata { Dates = [header], DetectedExtension = "mp4" })]).Single();

        Assert.Equal(MediaKind.Video, result.Kind);
        Assert.Equal("VID-20260917-222944.mp4", result.ProposedName);
    }

    [Fact]
    public void IgnoredSource_IsFlagged()
    {
        var exif = new DateCandidate(new DateTime(2023, 3, 4, 13, 20, 15), DatePrecision.Second, CandidateOrigin.ExifOriginal);

        var result = Engine().Normalize([Facts("/p/holiday.jpg", new RawMetadata { Dates = [exif], SourceWarnings = ["gps : valeur hors limites"] })]).Single();

        Assert.True(result.Decision.Flags.HasFlag(DecisionFlags.SourceIgnored));
        Assert.True(result.Decision.IsApplicable);
    }

    [Fact]
    public void LivePhotoConflict_IsFlaggedOnBothFiles()
    {
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 20, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));

        var results = Engine().Normalize(
        [
            Facts("/p/IMG_0477.HEIC", new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }),
            Facts("/p/IMG_0477.MOV", new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" })
        ]);

        Assert.All(results, r =>
        {
            Assert.True(r.Decision.Flags.HasFlag(DecisionFlags.LivePhotoConflict));
            Assert.False(r.Decision.Flags.HasFlag(DecisionFlags.LivePhoto));
            Assert.Null(r.LivePhotoPartner);
        });
        Assert.Equal("VID-20240711-200000.mov", results.Single(r => r.Kind == MediaKind.Video).ProposedName);
    }
}
