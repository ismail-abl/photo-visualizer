using PhotoApp.Core.Batches;
using PhotoApp.Core.Normalization;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class RenameWorkflowTests
{
    private static string Library(TempDirectory temp)
    {
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");   // EXIF seul : Probable
        File.WriteAllText(Path.Combine(photos, "holiday.xmp"), "<x/>");                      // son sidecar
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));                // WhatsApp : Sûre
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));                             // aucune date : jamais renommé
        TestVideos.Write(Path.Combine(photos, "20260917_222944.mp4"), new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");
        return photos;
    }

    private static IReadOnlyList<MediaResult> Analyze(string photos) => new MediaAnalyzer().Analyze([photos], []).Results;

    [Fact]
    public void ApplyThenUndo_GivesBackAnIdenticalState()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);

        var summary = workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        Assert.Equal(0, summary.Counts.Failed);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.xmp")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.True(File.Exists(Path.Combine(photos, "blank.jpg")));
        Assert.False(File.Exists(Path.Combine(photos, "holiday.jpg")));
        Assert.Equal(new DateTime(2023, 3, 4, 12, 20, 15, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(photos, "IMG-20230304-132015.jpg")));

        var undo = new BatchUndo(journal).Undo(summary.BatchId);

        Assert.True(undo.Completed);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Undone, journal.GetBatch(summary.BatchId)!.State);
    }

    [Fact]
    public void Apply_ThenPrepareAgain_FindsNothingLeftToRename()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var workflow = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne);
        workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        var second = workflow.Prepare(Analyze(photos), [photos]);

        Assert.Empty(second.Plan.Items);
        Assert.True(second.Plan.Summary.AlreadyConforming >= 2);
    }

    [Fact]
    public void Prepare_ATargetTakenByAFolder_IsSkipped()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        Directory.CreateDirectory(Path.Combine(photos, "IMG-20230304-132015.jpg"));

        var prepared = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne).Prepare(Analyze(photos), [photos]);

        Assert.Equal(2, prepared.Plan.Items.Count);
        Assert.Single(prepared.Skipped);
        Assert.Equal(1, prepared.Renamable);
        Assert.Equal("Le nom cible existe déjà", prepared.Skipped.Values.Single());
    }

    [Fact]
    public void Prepare_WritesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);

        new RenameWorkflow(journal, TestZones.PlusOne).Prepare(Analyze(photos), [photos]);

        Assert.Equal(before, Snapshot(photos));
        Assert.Empty(journal.ListBatches());
    }

    [Fact]
    public void Apply_AnEmptyPlan_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var workflow = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne);

        Assert.Throws<InvalidOperationException>(() => workflow.Apply(workflow.Prepare(Analyze(photos), [photos])));
    }

    [Fact]
    public void Apply_WhileABatchIsUnfinished_Throws_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [photos], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);
        var prepared = workflow.Prepare(Analyze(photos), [photos]);

        Assert.Throws<InvalidOperationException>(() => workflow.Apply(prepared));

        Assert.Equal(before, Snapshot(photos));
        Assert.Single(journal.ListBatches());
    }
}
