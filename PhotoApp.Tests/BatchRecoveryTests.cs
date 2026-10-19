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

    [Fact]
    public void Resolve_OnlyTheNewNameExists_AndTheDatesCannotBeReapplied_TheItemIsDoneWithAWarning_AndNothingThrows()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "pixels");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);
        File.Move(source, target);

        var report = new BatchRecovery(journal, (_, _, _) => throw new IOException("dates refusées")).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.StartsWith("dates non réappliquées", item.Error);
        Assert.Contains("dates refusées", item.Error);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(File.GetLastWriteTimeUtc(target), item.DoneLastWriteUtc);
        Assert.Equal(new RecoveryReport(1, 0, 0), report);
    }

    [Fact]
    public void Run_AfterACrash_AndTheDatesCannotBeReapplied_StillFinishesTheBatch()
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

        // Les dates ne se règlent plus du tout : la reprise ne doit pas faire échouer l'exécution, ni la bloquer pour toujours.
        var summary = new InPlaceRenamer(journal, FileMoves.Move, (_, _, _) => throw new IOException("dates refusées")).Run(batch);

        var items = journal.GetItems(batch);
        Assert.Equal(ItemState.Done, items[0].State);
        Assert.StartsWith("dates non réappliquées", items[0].Error);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-1.jpg")));
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(1, summary.Counts.Failed);   // IMG-2 : renommé puis remis sous son nom, les dates ne pouvant pas être réglées
        Assert.True(File.Exists(notReached));

        // Une nouvelle exécution du même lot est refusée (terminé) : plus rien ne reste « en cours ».
        Assert.Throws<InvalidOperationException>(() => new InPlaceRenamer(journal).Run(batch));
        Assert.Null(journal.FindUnfinished());
    }

    // Refuse à l'utilisateur courant la lecture du contenu du dossier (liste des fichiers), sans l'empêcher de modifier les droits.
    private static void DenyListing(string folder, bool deny)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var start = new System.Diagnostics.ProcessStartInfo("icacls") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in deny ? new[] { folder, "/deny", user + ":(RD)" } : [folder, "/remove:d", user])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Resolve_TheFolderCannotBeListed_TheItemFailsWithTheReason_AndNothingThrows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;   // droits NTFS : hors du champ de la phase 2 ailleurs que sous Windows
        }

        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "IMG-1.JPG");
        var (journal, batch, _) = Crashed(source, Path.Combine(photos, "IMG-1.jpg"), temp);   // changement de casse seul : la résolution liste le dossier

        RecoveryReport report;
        DenyListing(photos, deny: true);
        try
        {
            report = new BatchRecovery(journal).Resolve(batch);
        }
        finally
        {
            DenyListing(photos, deny: false);
        }

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.StartsWith("Résolution impossible", item.Error);
        Assert.Equal(new RecoveryReport(0, 0, 1), report);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Resolve_AfterTheDatesFailure_ASubsequentRunCompletesNormally()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);
        File.Move(source, target);
        new BatchRecovery(journal, (_, _, _) => throw new IOException("dates refusées")).Resolve(batch);

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.False(summary.Cancelled);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Null(journal.FindUnfinished());
    }
}
