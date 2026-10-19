using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class MediaNamerTests
{
    private sealed class FakeHasher(IReadOnlyDictionary<string, string>? hashes = null) : IContentHasher
    {
        public List<string> Calls { get; } = [];

        public string Hash(string fullPath)
        {
            Calls.Add(fullPath);
            return hashes?.GetValueOrDefault(fullPath) ?? fullPath;
        }
    }

    private static NamingInput Input(
        string path,
        DateTime? date,
        DatePrecision precision = DatePrecision.Second,
        MediaKind kind = MediaKind.Photo,
        string? extension = null,
        Confidence confidence = Confidence.Certain,
        long? counter = null,
        string? contentIdentifier = null)
    {
        var candidate = date is null ? null : new DateCandidate(date.Value, precision, CandidateOrigin.ExifOriginal);
        var decision = new DateDecision(candidate, confidence, Reasons.ExifOnly, [], []);
        return new NamingInput(path, kind, extension ?? Path.GetExtension(path).ToLowerInvariant(), decision, counter, contentIdentifier);
    }

    private static readonly DateTime Shot = new(2024, 1, 5, 15, 4, 12);

    private static IReadOnlyList<NamingResult> Name(params NamingInput[] inputs) => new MediaNamer(new FakeHasher()).Name(inputs);

    [Fact]
    public void PhotosAndVideos_GetTheirPrefix()
    {
        var results = Name(Input("/a/x.jpg", Shot), Input("/a/y.mp4", Shot.AddHours(1), kind: MediaKind.Video));

        Assert.Equal("IMG-20240105-150412.jpg", results[0].ProposedName);
        Assert.Equal("VID-20240105-160412.mp4", results[1].ProposedName);
    }

    [Fact]
    public void SameSecond_IsOrderedBySubSeconds()
    {
        var results = Name(Input("/a/late.jpg", Shot.AddMilliseconds(500), DatePrecision.SubSecond), Input("/a/early.jpg", Shot.AddMilliseconds(100), DatePrecision.SubSecond));

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Fact]
    public void SameDay_IsOrderedByTheNameCounter()
    {
        var day = new DateTime(2021, 9, 15);
        var results = Name(
            Input("/a/IMG-20210915-WA0002.jpg", day, DatePrecision.Day, counter: 2),
            Input("/a/IMG-20210915-WA0001.jpg", day, DatePrecision.Day, counter: 1));

        Assert.Equal("IMG-20210915-000000-02.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20210915-000000-01.jpg", results[1].ProposedName);
    }

    [Fact]
    public void RemainingTies_AreOrderedByContentHash_ComputedOnlyForThem()
    {
        var hasher = new FakeHasher(new Dictionary<string, string> { ["/a/b.jpg"] = "0001", ["/a/a.jpg"] = "0002" });

        var results = new MediaNamer(hasher).Name([Input("/a/a.jpg", Shot), Input("/a/b.jpg", Shot), Input("/a/c.jpg", Shot.AddMinutes(1))]);

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
        Assert.Equal(["/a/a.jpg", "/a/b.jpg"], hasher.Calls.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OtherFolders_DoNotCollide()
    {
        var results = Name(Input("/a/x.jpg", Shot), Input("/b/x.jpg", Shot));

        Assert.Equal("IMG-20240105-150412.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Fact]
    public void AlreadyNormalizedName_IsKept_AndNeverRenumbered()
    {
        var results = Name(Input("/a/IMG-20240105-150412-01.jpg", Shot), Input("/a/new.jpg", Shot));

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Theory]
    [InlineData("/a/IMG-20240105-150413.jpg")]      // autre date
    [InlineData("/a/IMG-20240105-150412.JPG")]      // extension non normalisée
    [InlineData("/a/VID-20240105-150412.jpg")]      // mauvais préfixe
    public void NonConformingNames_AreRenamed(string path)
    {
        Assert.Equal("IMG-20240105-150412.jpg", Name(Input(path, Shot, extension: ".jpg"))[0].ProposedName);
    }

    [Fact]
    public void DayOnlyNameWithoutSuffix_IsRenamed()
    {
        Assert.Equal("IMG-20210915-000000-01.jpg", Name(Input("/a/IMG-20210915-000000.jpg", new DateTime(2021, 9, 15), DatePrecision.Day))[0].ProposedName);
    }

    [Theory]
    [InlineData(Confidence.Uncertain)]
    [InlineData(Confidence.None)]
    public void NotApplicable_GetsNoName(Confidence confidence)
    {
        Assert.Null(Name(Input("/a/x.jpg", Shot, confidence: confidence))[0].ProposedName);
    }

    [Fact]
    public void LivePhoto_ByContentIdentifier_VideoFollowsThePhoto()
    {
        var results = Name(
            Input("/a/IMG_0477.HEIC", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.MOV", Shot.AddSeconds(1), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.Equal("IMG_0478.MOV", results[0].LivePhotoPartner);
        Assert.Equal("IMG_0477.HEIC", results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_BySameBaseName()
    {
        var results = Name(
            Input("/a/24-07-11 17-07-10 0477.jpg", Shot),
            Input("/a/24-07-11 17-07-10 0477.mov", Shot, kind: MediaKind.Video));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
    }

    [Fact]
    public void LivePhoto_WithoutAPhotoName_LeavesTheVideoUnnamed()
    {
        var results = Name(
            Input("/a/IMG_0477.jpg", Shot, confidence: Confidence.Uncertain),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video));

        Assert.Null(results[1].ProposedName);
        Assert.Equal("IMG_0477.jpg", results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_BySameBaseName_WithDifferentContentIdentifiers_IsNotPaired()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "AAA"),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video, extension: ".mov", contentIdentifier: "BBB"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("VID-20240105-150412.mov", results[1].ProposedName);
        Assert.Null(results[0].LivePhotoPartner);
        Assert.Null(results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_BySameBaseName_WhenOnlyOneSideHasAnIdentifier_IsStillPaired()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "AAA"),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video, extension: ".mov"));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.Equal("IMG_0477.heic", results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_WhenTheVideoDateDisagrees_EachFileFollowsItsOwnDecision_AndTheConflictIsReported()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.mov", Shot.AddHours(3), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("VID-20240105-180412.mov", results[1].ProposedName);
        Assert.Null(results[0].LivePhotoPartner);
        Assert.Null(results[1].LivePhotoPartner);
        Assert.True(results[0].LivePhotoConflict);
        Assert.True(results[1].LivePhotoConflict);
    }

    [Fact]
    public void LivePhoto_WhenTheVideoHasNoApplicableDecision_StillFollowsThePhoto()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.mov", Shot.AddHours(3), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC", confidence: Confidence.Uncertain));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.False(results[1].LivePhotoConflict);
    }
}
