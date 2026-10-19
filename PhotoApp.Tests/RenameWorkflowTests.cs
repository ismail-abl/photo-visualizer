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

    // Horloge et fuseau figés : ni l'heure du PC ni son fuseau n'entrent dans le résultat.
    private static readonly NormalizationOptions Options = new(new DateTime(2026, 10, 19, 12, 0, 0), TestZones.PlusOne);

    private static IReadOnlyList<MediaResult> Analyze(string photos) => new MediaAnalyzer().Analyze([photos], [], options: Options).Results;

    [Fact]
    public void ApplyThenUndo_GivesBackAnIdenticalState()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);

        var summary = workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        // État intermédiaire : exactement quatre fichiers renommés (photo, son sidecar, WhatsApp, vidéo), le fichier sans date intact.
        Assert.Equal(0, summary.Counts.Failed);
        Assert.Equal(4, summary.Counts.Done);
        Assert.Equal(0, summary.Counts.Skipped);
        string[] renamed = ["IMG-20210915-000000-01.png", "IMG-20230304-132015.jpg", "IMG-20230304-132015.xmp", "VID-20260917-222944.mp4"];
        Assert.Equal([.. renamed, "blank.jpg"], new DirectoryInfo(photos).EnumerateFiles().Select(file => file.Name).Order(StringComparer.Ordinal));
        Assert.Equal(before["blank.jpg"], Snapshot(photos)["blank.jpg"]);

        // Chaque fichier renommé porte, en création comme en modification, l'instant enregistré au journal (lui-même issu du plan).
        var items = journal.GetItems(summary.BatchId);
        Assert.Equal(renamed, items.Select(item => Path.GetFileName(item.TargetPath)).Order(StringComparer.Ordinal));
        Assert.All(items, item =>
        {
            Assert.Equal(ItemState.Done, item.State);
            Assert.False(File.Exists(item.SourcePath));
            Assert.Equal(item.DecidedDate.UtcDateTime, File.GetCreationTimeUtc(item.TargetPath));
            Assert.Equal(item.DecidedDate.UtcDateTime, File.GetLastWriteTimeUtc(item.TargetPath));
        });

        var photo = Path.Combine(photos, "IMG-20230304-132015.jpg");
        var decidedPhoto = new DateTime(2023, 3, 4, 12, 20, 15, DateTimeKind.Utc);
        Assert.Equal(decidedPhoto, File.GetCreationTimeUtc(photo));
        Assert.Equal(decidedPhoto, File.GetLastWriteTimeUtc(photo));

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
        Assert.Equal(3, second.Plan.Summary.AlreadyConforming);
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
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);

        Assert.Throws<InvalidOperationException>(() => workflow.Apply(workflow.Prepare(Analyze(photos), [photos])));

        Assert.Empty(journal.ListBatches());
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
        Assert.Equal(stuck, Assert.Single(journal.ListBatches()).Id);
        journal.AcquireExecutionLock().Dispose();   // le verrou a bien été relâché malgré le refus
    }

    [Fact]
    public void Apply_WhileTheExecutionLockIsHeld_Throws_AndCreatesNoBatch()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);
        var prepared = workflow.Prepare(Analyze(photos), [photos]);

        using var held = journal.AcquireExecutionLock();

        Assert.Throws<BatchLockedException>(() => workflow.Apply(prepared));

        Assert.Empty(journal.ListBatches());
        Assert.Equal(before, Snapshot(photos));
    }

    [Fact]
    public void Apply_ReleasesTheExecutionLock()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);

        workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        journal.AcquireExecutionLock().Dispose();
    }
}
