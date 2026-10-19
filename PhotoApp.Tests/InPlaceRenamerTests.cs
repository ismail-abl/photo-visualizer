using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class InPlaceRenamerTests
{
    [Fact]
    public void Run_RenamesTheFile_SetsTheDecidedDates_AndKeepsTheContent()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "holiday.jpg", "pixels");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-20230304-132015.jpg"), 0));

        var summary = new InPlaceRenamer(journal).Run(batch);

        var target = Path.Combine(photos, "IMG-20230304-132015.jpg");
        Assert.False(File.Exists(source));
        Assert.Equal("pixels", File.ReadAllText(target));
        Assert.Equal(Decided.UtcDateTime, File.GetLastWriteTimeUtc(target));
        Assert.Equal(Decided.UtcDateTime, File.GetCreationTimeUtc(target));
        Assert.False(summary.Cancelled);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(Decided.UtcDateTime, item.DoneLastWriteUtc);
    }

    [Fact]
    public void Run_CaseOnlyChange_RenamesTheFile()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "IMG-20230304-132015.JPG");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-20230304-132015.jpg"), 0));

        new InPlaceRenamer(journal).Run(batch);

        Assert.Equal(["IMG-20230304-132015.jpg"], Directory.GetFiles(photos).Select(Path.GetFileName));
        Assert.Equal(ItemState.Done, journal.GetItems(batch).Single().State);
    }

    [Fact]
    public void Run_FileChangedSinceThePlan_IsSkipped_AndLeftUntouched()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "holiday.jpg", "v1");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));
        File.WriteAllText(source, "version modifiée");

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(photos, "IMG-1.jpg")));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Skipped, item.State);
        Assert.Equal("Fichier modifié depuis le plan", item.Error);
        Assert.Equal(1, summary.Counts.Skipped);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_TargetTaken_SkipsTheWholeGroup_AndNeverOverwrites()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var media = WriteFile(photos, "holiday.jpg", "photo");
        var sidecar = WriteFile(photos, "holiday.xmp", "sidecar");
        WriteFile(photos, "IMG-1.xmp", "propriétaire du nom");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(media, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(sidecar, Path.Combine(photos, "IMG-1.xmp"), 1, group: 1, role: ItemRole.Sidecar));

        new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(media));
        Assert.True(File.Exists(sidecar));
        Assert.Equal("propriétaire du nom", File.ReadAllText(Path.Combine(photos, "IMG-1.xmp")));
        Assert.All(journal.GetItems(batch), item => Assert.Equal(ItemState.Skipped, item.State));
    }

    [Fact]
    public void Run_AFailureOnOneFile_DoesNotStopTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var failing = WriteFile(photos, "a.jpg");
        var fine = WriteFile(photos, "b.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(failing, Path.Combine(photos, "absent", "IMG-1.jpg"), 0, group: 1),   // le dossier cible n'existe pas
            DraftFor(fine, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(failing));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-2.jpg")));
        var items = journal.GetItems(batch);
        Assert.Equal(ItemState.Failed, items[0].State);
        Assert.False(string.IsNullOrEmpty(items[0].Error));
        Assert.Equal(ItemState.Done, items[1].State);
        Assert.Equal(1, summary.Counts.Failed);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_Cancelled_FinishesTheCurrentFile_ThenStops_AndCanBeResumed()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var drafts = Enumerable.Range(0, 3)
            .Select(i => DraftFor(WriteFile(photos, $"a{i}.jpg"), Path.Combine(photos, $"IMG-{i}.jpg"), i, group: i + 1))
            .ToArray();
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, drafts);
        using var cancellation = new CancellationTokenSource();

        var first = new InPlaceRenamer(journal).Run(batch, cancellation.Token, new SyncProgress(_ => cancellation.Cancel()));

        Assert.True(first.Cancelled);
        Assert.Equal(1, first.Counts.Done);
        Assert.Equal(2, first.Counts.Pending);
        Assert.Equal(BatchState.Interrupted, journal.GetBatch(batch)!.State);
        Assert.Equal(batch, journal.FindUnfinished()?.Id);

        var second = new InPlaceRenamer(journal).Run(batch);

        Assert.False(second.Cancelled);
        Assert.Equal(3, second.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void Run_ACompletedBatch_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        new InPlaceRenamer(journal).Run(batch);

        Assert.Throws<InvalidOperationException>(() => new InPlaceRenamer(journal).Run(batch));
    }

    [Fact]
    public void Run_WhileAnotherTreatmentHoldsTheLock_Throws_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));

        using var held = journal.AcquireExecutionLock();

        Assert.Throws<BatchLockedException>(() => new InPlaceRenamer(journal).Run(batch));
        Assert.True(File.Exists(source));
        Assert.Equal(BatchState.Planned, journal.GetBatch(batch)!.State);
    }

    // Dates d'origine distinctes de la date décidée : un retour arrière qui oublierait de les restaurer se verrait.
    private static readonly DateTime OriginalCreation = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime OriginalWrite = new(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);

    private static string WriteFileWithOriginalDates(string folder, string name, string content)
    {
        var path = WriteFile(folder, name, content);
        FileTimestamps.Set(path, OriginalCreation, OriginalWrite);
        return path;
    }

    [Fact]
    public void Run_DatesCannotBeSet_TheFileGoesBackToItsOriginalName_AndTheItemFails()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFileWithOriginalDates(photos, "holiday.jpg", "pixels");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));

        var summary = new InPlaceRenamer(journal, FileMoves.Move, (_, _, _) => throw new IOException("dates refusées")).Run(batch);

        Assert.Equal("pixels", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        Assert.Equal(OriginalCreation, File.GetCreationTimeUtc(source));
        Assert.Equal(OriginalWrite, File.GetLastWriteTimeUtc(source));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Contains("dates refusées", item.Error);
        Assert.Contains("renommage annulé", item.Error);
        Assert.Equal(1, summary.Counts.Failed);
        Assert.Equal(0, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_DatesPartiallyChanged_TheRollbackRestoresTheOriginalDates()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFileWithOriginalDates(photos, "holiday.jpg", "pixels");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        var calls = 0;

        // Premier appel : la date de modification change, puis l'échec. Second appel (la restauration) : le vrai réglage.
        void SetTimes(string path, DateTime creation, DateTime lastWrite)
        {
            if (++calls == 1)
            {
                File.SetLastWriteTimeUtc(path, lastWrite);
                throw new IOException("dates refusées");
            }

            FileTimestamps.Set(path, creation, lastWrite);
        }

        new InPlaceRenamer(journal, FileMoves.Move, SetTimes).Run(batch);

        Assert.Equal(2, calls);
        Assert.Equal("pixels", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        Assert.Equal(OriginalCreation, File.GetCreationTimeUtc(source));
        Assert.Equal(OriginalWrite, File.GetLastWriteTimeUtc(source));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Contains("renommage annulé", item.Error);
        Assert.DoesNotContain("non restaurées", item.Error);
    }

    [Fact]
    public void Run_DatesCannotBeSet_NorRestored_TheFileIsBackUnderItsOriginalName_AndTheMessageSaysSo()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFileWithOriginalDates(photos, "holiday.jpg", "pixels");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));

        // Chaque appel change la date de modification puis échoue : la date a bien bougé, et la restauration échoue aussi.
        void SetTimes(string path, DateTime creation, DateTime lastWrite)
        {
            File.SetLastWriteTimeUtc(path, lastWrite);
            throw new IOException("dates refusées");
        }

        new InPlaceRenamer(journal, FileMoves.Move, SetTimes).Run(batch);

        // Le fichier est sous son ancien nom : c'est le déplacement, pas les dates, qui dit où il est.
        Assert.Equal("pixels", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Contains("dates d'origine non restaurées", item.Error);
        Assert.Contains("dates refusées", item.Error);
        Assert.DoesNotContain("retour arrière impossible", item.Error);
    }

    [Fact]
    public void Run_DatesCannotBeSet_AndTheMoveBackFails_TheItemIsDoneWithAWarning_SoTheUndoCanStillFindIt()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFileWithOriginalDates(photos, "holiday.jpg", "pixels");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        var moves = 0;

        void MoveFile(string from, string to)
        {
            if (++moves == 2)
            {
                throw new IOException("verrou");   // le déplacement de retour
            }

            FileMoves.Move(from, to);
        }

        var summary = new InPlaceRenamer(journal, MoveFile, (_, _, _) => throw new IOException("dates refusées")).Run(batch);

        // Le fichier est renommé (sous le nouveau nom), seules ses dates n'ont pas été appliquées.
        Assert.False(File.Exists(source));
        Assert.Equal("pixels", File.ReadAllText(target));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.Contains("dates non appliquées", item.Error);
        Assert.Contains("dates refusées", item.Error);
        Assert.Contains("retour arrière impossible", item.Error);
        Assert.Contains("verrou", item.Error);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(File.GetLastWriteTimeUtc(target), item.DoneLastWriteUtc);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(0, summary.Counts.Failed);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_TheMoveFails_TheItemFailsWithTheMessage_AndTheDatesAreNeverTouched()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFileWithOriginalDates(photos, "holiday.jpg", "pixels");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));
        var datesTouched = false;

        new InPlaceRenamer(journal, (_, _) => throw new IOException("déplacement refusé"), (_, _, _) => datesTouched = true).Run(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Equal("déplacement refusé", item.Error);
        Assert.False(datesTouched);
        Assert.Equal("pixels", File.ReadAllText(source));
    }
}
