using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class BatchUndoTests
{
    [Fact]
    public void Undo_RestoresNamesContentsAndDates()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var first = WriteFile(photos, "a.jpg", "A");
        var second = WriteFile(photos, "b.jpg", "BB");
        File.SetLastWriteTimeUtc(second, new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        File.SetCreationTimeUtc(second, new DateTime(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(first, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(second, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));
        new InPlaceRenamer(journal).Run(batch);
        Assert.NotEqual(before.Keys.Order(), Snapshot(photos).Keys.Order());

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(new UndoSummary(batch, 2, 0, true), summary);
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
        Assert.All(journal.GetItems(batch), item => Assert.Equal(ItemState.Undone, item.State));
    }

    [Fact]
    public void Undo_ALaterEditedFile_IsLeftInPlace_AndReported()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "v1");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);
        File.WriteAllText(target, "retouchée depuis le lot");

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.Equal("retouchée depuis le lot", File.ReadAllText(target));
        Assert.False(File.Exists(source));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.UndoFailed, item.State);
        Assert.StartsWith("Fichier modifié depuis le lot", item.Error);
        Assert.Equal(new UndoSummary(batch, 0, 1, false), summary);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Equal(batch, journal.FindLastUndoable()?.Id);
    }

    [Fact]
    public void Undo_OriginalNameTaken_IsReported_ThenRetriedOnceFree()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "A");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);
        WriteFile(photos, "a.jpg", "un autre fichier");

        var blocked = new BatchUndo(journal).Undo(batch);

        Assert.False(blocked.Completed);
        Assert.Equal("un autre fichier", File.ReadAllText(source));
        Assert.True(File.Exists(target));
        Assert.StartsWith("Le nom d'origine est occupé", journal.GetItems(batch).Single().Error);

        File.Delete(source);
        var retried = new BatchUndo(journal).Undo(batch);

        Assert.True(retried.Completed);
        Assert.Equal("A", File.ReadAllText(source));
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Undo_AnAlreadyUndoneBatch_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        new InPlaceRenamer(journal).Run(batch);
        new BatchUndo(journal).Undo(batch);

        Assert.Throws<InvalidOperationException>(() => new BatchUndo(journal).Undo(batch));
    }

    [Fact]
    public void Undo_ABatchThatNeverRan_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));

        Assert.Throws<InvalidOperationException>(() => new BatchUndo(journal).Undo(batch));
    }

    [Fact]
    public void Undo_AnInterruptedBatch_RestoresWhatWasDone_AndSkipsTheRest()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var drafts = Enumerable.Range(0, 3)
            .Select(i => DraftFor(WriteFile(photos, $"a{i}.jpg"), Path.Combine(photos, $"IMG-{i}.jpg"), i, group: i + 1))
            .ToArray();
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, drafts);
        using var cancellation = new CancellationTokenSource();
        new InPlaceRenamer(journal).Run(batch, cancellation.Token, new SyncProgress(_ => cancellation.Cancel()));

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.True(summary.Completed);
        Assert.Equal(1, summary.Restored);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
        Assert.Equal(2, journal.GetBatch(batch)!.Counts.Skipped);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void Undo_ACrashedBatch_ResolvesTheRunningItemFirst()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        journal.SetBatchState(batch, BatchState.Running);
        journal.SetItemState(journal.GetItems(batch).Single().Id, ItemState.Running);
        File.Move(source, target);

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.True(summary.Completed);
        Assert.Equal(before, Snapshot(photos));
    }

    [Fact]
    public void Undo_WhenOnlyTheDatesRestoreFails_TheNameIsRestored_AndTheItemIsUndoneWithAWarning()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "A");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);

        var summary = new BatchUndo(journal, (_, _, _) => throw new IOException("verrouillé")).Undo(batch);

        // Le déplacement de retour a réussi : le fichier a retrouvé son nom d'origine, seules ses dates n'ont pas été restaurées.
        Assert.Equal("A", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Undone, item.State);
        Assert.StartsWith("nom restauré, dates d'origine non restaurées", item.Error);
        Assert.Contains("verrouillé", item.Error);
        Assert.Equal(new UndoSummary(batch, 1, 0, true), summary);
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
    }
}
