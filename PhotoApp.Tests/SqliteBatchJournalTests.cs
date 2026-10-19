using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SqliteBatchJournalTests
{
    private static readonly DateTime Created = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static SqliteBatchJournal NewJournal(TempDirectory temp) => new(new CatalogDatabase(temp.Combine("catalog.db")));

    private static BatchItemDraft Draft(int rank, int group = 1, ItemRole role = ItemRole.Media) => new(
        rank,
        group,
        role,
        $@"C:\photos\a{rank}.jpg",
        $@"C:\photos\IMG-{rank}.jpg",
        new DateTimeOffset(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1)),
        100 + rank,
        new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 3, 4, 5, DateTimeKind.Utc));

    [Fact]
    public void CreateBatch_StoresTheBatchAndItsItems_AsPending()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);

        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0), Draft(1, role: ItemRole.Sidecar)]);

        var batch = journal.GetBatch(id)!;
        Assert.Equal(BatchState.Planned, batch.State);
        Assert.Equal(Created, batch.CreatedUtc);
        Assert.Equal([@"C:\photos"], batch.Folders);
        Assert.Equal(new BatchCounts(2, 2, 0, 0, 0, 0, 0, 0), batch.Counts);

        var items = journal.GetItems(id);
        Assert.Equal([0, 1], items.Select(i => i.Rank));
        Assert.Equal(ItemRole.Sidecar, items[1].Role);
        Assert.All(items, i => Assert.Equal(ItemState.Pending, i.State));
        var first = items[0];
        var draft = Draft(0);
        Assert.Equal(draft.SourcePath, first.SourcePath);
        Assert.Equal(draft.TargetPath, first.TargetPath);
        Assert.Equal(draft.DecidedDate, first.DecidedDate);
        Assert.Equal(draft.DecidedDate.Offset, first.DecidedDate.Offset);
        Assert.Equal(draft.SourceSize, first.SourceSize);
        Assert.Equal(draft.SourceLastWriteUtc, first.SourceLastWriteUtc);
        Assert.Equal(draft.SourceCreationUtc, first.SourceCreationUtc);
        Assert.Null(first.DoneSize);
        Assert.Null(first.DoneLastWriteUtc);
    }

    [Fact]
    public void GetBatch_UnknownId_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(NewJournal(temp).GetBatch(42));
    }

    [Fact]
    public void SetItemState_ChangesTheCounts_AndKeepsTheError()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0), Draft(1), Draft(2)]);
        var items = journal.GetItems(id);

        journal.SetItemState(items[0].Id, ItemState.Running);
        journal.SetItemState(items[1].Id, ItemState.Failed, "verrouillé");
        journal.SetItemState(items[2].Id, ItemState.Skipped, "introuvable");

        var after = journal.GetItems(id);
        Assert.Equal("verrouillé", after[1].Error);
        Assert.Equal(new BatchCounts(3, 0, 1, 0, 1, 1, 0, 0), journal.GetBatch(id)!.Counts);
    }

    [Fact]
    public void SetItemDone_RecordsTheOutcome()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        var item = journal.GetItems(id).Single();
        var written = new DateTime(2023, 3, 4, 12, 20, 15, DateTimeKind.Utc);

        journal.SetItemDone(item.Id, 123, written, "dates non appliquées");

        var done = journal.GetItems(id).Single();
        Assert.Equal(ItemState.Done, done.State);
        Assert.Equal(123, done.DoneSize);
        Assert.Equal(written, done.DoneLastWriteUtc);
        Assert.Equal("dates non appliquées", done.Error);
    }

    [Fact]
    public void SetBatchState_IsPersisted_AcrossReopening()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(id, BatchState.Running);

        var reopened = NewJournal(temp);

        Assert.Equal(BatchState.Running, reopened.GetBatch(id)!.State);
        Assert.Equal(id, reopened.FindUnfinished()?.Id);
    }

    [Fact]
    public void FindUnfinished_ReturnsTheMostRecentRunningOrInterruptedBatch()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var completed = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(completed, BatchState.Completed);
        var interrupted = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(interrupted, BatchState.Interrupted);

        Assert.Equal(interrupted, journal.FindUnfinished()?.Id);

        journal.SetBatchState(interrupted, BatchState.Undone);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void FindLastUndoable_SkipsBatchesWithoutDoneItems_AndUndoneBatches()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var first = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetItemDone(journal.GetItems(first).Single().Id, 1, Created);
        journal.SetBatchState(first, BatchState.Completed);
        var emptyOutcome = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetItemState(journal.GetItems(emptyOutcome).Single().Id, ItemState.Failed, "x");
        journal.SetBatchState(emptyOutcome, BatchState.Completed);

        Assert.Equal(first, journal.FindLastUndoable()?.Id);

        journal.SetBatchState(first, BatchState.Undone);
        Assert.Null(journal.FindLastUndoable());
    }

    [Fact]
    public void ListBatches_AreOrderedFromTheOldestToTheNewest()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var first = journal.CreateBatch(Created, [@"C:\a"], [Draft(0)]);
        var second = journal.CreateBatch(Created, [@"C:\b"], [Draft(0)]);

        Assert.Equal([first, second], journal.ListBatches().Select(b => b.Id));
    }

    [Fact]
    public void ExecutionLock_IsExclusive_UntilReleased()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);

        var held = journal.AcquireExecutionLock();
        Assert.Throws<BatchLockedException>(() => journal.AcquireExecutionLock());

        held.Dispose();
        using var again = journal.AcquireExecutionLock();
    }
}
