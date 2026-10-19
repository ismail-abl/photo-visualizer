using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class BatchRecoveryTests
{
    // Un lot tué au milieu d'un fichier : élément « en cours », lot « en cours ».
    private static (SqliteBatchJournal Journal, long Batch, BatchItem Item) Crashed(string source, string target, TempDirectory temp)
    {
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        journal.SetBatchState(batch, BatchState.Running);
        var item = journal.GetItems(batch).Single();
        journal.SetItemState(item.Id, ItemState.Running);
        return (journal, batch, journal.GetItems(batch).Single());
    }

    [Fact]
    public void Resolve_OnlyTheNewNameExists_TheItemIsDone_AndTheDatesAreReapplied()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);
        File.Move(source, target);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.Equal(Decided.UtcDateTime, File.GetLastWriteTimeUtc(target));
        Assert.Equal(Decided.UtcDateTime, item.DoneLastWriteUtc);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(new RecoveryReport(1, 0, 0), report);
    }

    [Fact]
    public void Resolve_OnlyTheOldNameExists_TheItemGoesBackToPending()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var (journal, batch, _) = Crashed(source, Path.Combine(photos, "IMG-1.jpg"), temp);

        var report = new BatchRecovery(journal).Resolve(batch);

        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);
        Assert.True(File.Exists(source));
        Assert.Equal(new RecoveryReport(0, 1, 0), report);
    }

    [Fact]
    public void Resolve_BothNamesExist_IsAnAnomaly_AndNothingIsOverwritten()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "A");
        var target = WriteFile(photos, "IMG-1.jpg", "B");
        var (journal, batch, _) = Crashed(source, target, temp);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.StartsWith("Les deux noms existent", item.Error);
        Assert.Equal("A", File.ReadAllText(source));
        Assert.Equal("B", File.ReadAllText(target));
        Assert.Equal(new RecoveryReport(0, 0, 1), report);
    }

    [Fact]
    public void Resolve_NeitherNameExists_IsAnAnomaly()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var (journal, batch, _) = Crashed(source, Path.Combine(photos, "IMG-1.jpg"), temp);
        File.Delete(source);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Equal("Fichier introuvable", item.Error);
        Assert.Equal(1, report.Anomalies);
    }

    [Fact]
    public void Resolve_CaseOnlyRename_ReadsTheRealCaseOnDisk()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "IMG-1.JPG");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);

        // Le nom porte encore l'ancienne casse : le renommage n'a pas eu lieu.
        new BatchRecovery(journal).Resolve(batch);
        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);

        // Le nom porte la nouvelle casse : il a eu lieu.
        journal.SetItemState(journal.GetItems(batch).Single().Id, ItemState.Running);
        File.Move(source, source + ".tmp");
        File.Move(source + ".tmp", target);
        new BatchRecovery(journal).Resolve(batch);
        Assert.Equal(ItemState.Done, journal.GetItems(batch).Single().State);
    }

    [Fact]
    public void Resolve_LeavesOtherItemsUntouched()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        journal.SetBatchState(batch, BatchState.Interrupted);

        var report = new BatchRecovery(journal).Resolve(batch);

        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);
        Assert.Equal(new RecoveryReport(0, 0, 0), report);
    }

    [Fact]
    public void Run_AfterACrash_ResolvesThenFinishesTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var movedBeforeTheCrash = WriteFile(photos, "a.jpg");
        var notReached = WriteFile(photos, "b.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(movedBeforeTheCrash, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(notReached, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));
        journal.SetBatchState(batch, BatchState.Running);
        journal.SetItemState(journal.GetItems(batch)[0].Id, ItemState.Running);
        File.Move(movedBeforeTheCrash, Path.Combine(photos, "IMG-1.jpg"));

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.Equal(2, summary.Counts.Done);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-1.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-2.jpg")));
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }
}
