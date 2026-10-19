using PhotoApp.Core.Batches;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class BatchPlannerTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 19, 12, 0, 0);
    private static readonly DateTime LastWrite = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private sealed class NoHasher : IContentHasher
    {
        public string Hash(string fullPath) => fullPath;
    }

    private static IReadOnlyList<MediaResult> Analyze(params MediaFacts[] facts) =>
        new NormalizationEngine(new NormalizationOptions(UtcNow, TestZones.PlusOne), new NoHasher()).Normalize(facts);

    private static MediaFacts Facts(string path, RawMetadata? metadata = null, IReadOnlyList<string>? sidecars = null) =>
        new(new ScannedMedia(path, 100, LastWrite, sidecars ?? []), metadata ?? RawMetadata.Empty);

    private static RawMetadata Exif(DateTime wallClock, string? extension = null, TimeSpan? offset = null) => new()
    {
        Dates = [new DateCandidate(wallClock, DatePrecision.Second, CandidateOrigin.ExifOriginal, offset)],
        DetectedExtension = extension
    };

    private static BatchPlan Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null) =>
        new BatchPlanner(TestZones.PlusOne).Plan(results, maxGroups);

    [Fact]
    public void Plan_KeepsSureAndProbable_AndLeavesTheRest()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))),              // EXIF seul : Probable
            Facts(temp.Combine("IMG-20210915-WA0005.png")),                                               // WhatsApp : Sûre
            Facts(temp.Combine("blank.jpg")));                                                            // rien : non applicable

        var plan = Plan(results);

        var targets = plan.Items.ToDictionary(item => Path.GetFileName(item.SourcePath), item => Path.GetFileName(item.TargetPath));
        Assert.Equal(2, targets.Count);
        Assert.Equal("IMG-20230304-132015.jpg", targets["holiday.jpg"]);
        Assert.Equal("IMG-20210915-000000-01.png", targets["IMG-20210915-WA0005.png"]);
        Assert.Equal(new PlanSummary(0, 2, 2, 0), plan.Summary);
    }

    [Fact]
    public void Plan_AlreadyConformingFiles_ProduceNoItem()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("IMG-20240102-030405.jpg")),
            Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))));

        var plan = Plan(results);

        Assert.Equal(["holiday.jpg"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(1, plan.Summary.AlreadyConforming);
    }

    [Fact]
    public void Plan_RecordsTheDecidedInstantAndTheOriginalState()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("holiday.jpg");
        File.WriteAllText(path, "contenu");
        var creation = new FileInfo(path).CreationTimeUtc;
        var results = Analyze(new MediaFacts(new ScannedMedia(path, 7, LastWrite, []), Exif(new DateTime(2023, 3, 4, 13, 20, 15))));

        var item = Plan(results).Items.Single();

        Assert.Equal(new DateTimeOffset(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1)), item.DecidedDate);
        Assert.Equal(7, item.SourceSize);
        Assert.Equal(LastWrite, item.SourceLastWriteUtc);
        Assert.Equal(creation, item.SourceCreationUtc);
        Assert.Equal(0, item.Rank);
        Assert.Equal(ItemRole.Media, item.Role);
    }

    [Fact]
    public void Plan_SidecarsFollowTheirMedia_InTheSameGroup()
    {
        using var temp = new TempDirectory();
        var sidecar = temp.Combine("holiday.xmp");
        File.WriteAllText(sidecar, "<x/>");
        var results = Analyze(Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15)), [sidecar]));

        var items = Plan(results).Items;

        Assert.Equal(["holiday.jpg", "holiday.xmp"], items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(["IMG-20230304-132015.jpg", "IMG-20230304-132015.xmp"], items.Select(item => Path.GetFileName(item.TargetPath)));
        Assert.Equal([ItemRole.Media, ItemRole.Sidecar], items.Select(item => item.Role));
        Assert.Single(items.Select(item => item.GroupId).Distinct());
        Assert.Equal([0, 1], items.Select(item => item.Rank));
    }

    [Fact]
    public void Plan_ASidecarSharedByTheTwoHalvesOfALivePhoto_IsPlannedOnce()
    {
        using var temp = new TempDirectory();
        var aae = temp.Combine("IMG_0477.AAE");
        File.WriteAllText(aae, "x");
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var results = Analyze(
            Facts(temp.Combine("IMG_0477.HEIC"), new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }, [aae]),
            Facts(temp.Combine("IMG_0477.MOV"), new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" }, [aae]));

        var plan = Plan(results);

        // La photo, son sidecar (réclamé par la photo), puis la vidéo.
        Assert.Equal(["IMG_0477.HEIC", "IMG_0477.AAE", "IMG_0477.MOV"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(["IMG-20240711-170710.heic", "IMG-20240711-170710.AAE", "IMG-20240711-170710.mov"], plan.Items.Select(item => Path.GetFileName(item.TargetPath)));
        Assert.Equal([ItemRole.Media, ItemRole.Sidecar, ItemRole.LivePartner], plan.Items.Select(item => item.Role));
        Assert.Single(plan.Items.Select(item => item.GroupId).Distinct());
        Assert.Equal(new PlanSummary(0, 3, 1, 1), plan.Summary);   // la HEIC est comptée « conversion différée »
    }

    [Fact]
    public void Plan_MaxGroups_KeepsTheFirstGroupsOnly()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("a.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))),
            Facts(temp.Combine("b.jpg"), Exif(new DateTime(2023, 3, 5, 13, 20, 15))),
            Facts(temp.Combine("c.jpg"), Exif(new DateTime(2023, 3, 6, 13, 20, 15))));

        var plan = Plan(results, maxGroups: 2);

        Assert.Equal(["a.jpg", "b.jpg"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(2, plan.Summary.Groups);
    }

    [Fact]
    public void Plan_Nothing_ToRename_IsAnEmptyPlan()
    {
        var plan = Plan(Analyze());

        Assert.Empty(plan.Items);
        Assert.Equal(new PlanSummary(0, 0, 0, 0), plan.Summary);
    }
}
