using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

public sealed record BatchProgress(int Processed, int Total);

public sealed record BatchRunSummary(long BatchId, BatchCounts Counts, bool Cancelled);

/// <summary>
/// Exécute un lot : renommage sur place, un seul déplacement par fichier, journal écrit avant chaque action
/// (spec de phase 2, §5.3). Un échec sur un fichier n'arrête pas le lot ; l'arrêt demandé termine le fichier en cours.
/// </summary>
public sealed class InPlaceRenamer
{
    private readonly IBatchJournal journal;
    private readonly Action<string, string> moveFile;
    private readonly Action<string, DateTime, DateTime> setTimes;

    public InPlaceRenamer(IBatchJournal journal)
        : this(journal, FileMoves.Move, FileTimestamps.Set)
    {
    }

    // Déplacement et réglage des dates injectables : seul moyen de provoquer, dans les tests, les échecs tardifs d'un système de fichiers.
    internal InPlaceRenamer(IBatchJournal journal, Action<string, string> moveFile, Action<string, DateTime, DateTime> setTimes)
    {
        this.journal = journal;
        this.moveFile = moveFile;
        this.setTimes = setTimes;
    }

    public BatchRunSummary Run(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        using var executionLock = journal.AcquireExecutionLock();
        return RunWithLockHeld(batchId, cancellation, progress);
    }

    // L'appelant doit déjà tenir le verrou d'exécution (IBatchJournal.AcquireExecutionLock) : ce verrou n'est pas réentrant.
    internal BatchRunSummary RunWithLockHeld(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        var batch =journal.GetBatch(batchId) ?? throw new InvalidOperationException($"Lot {batchId} introuvable.");
        if (batch.State is BatchState.Completed or BatchState.Undone)
        {
            throw new InvalidOperationException($"Le lot {batchId} est déjà terminé.");
        }

        journal.SetBatchState(batchId, BatchState.Running);
        new BatchRecovery(journal, setTimes).Resolve(batchId);
        SkipFailingEntries(batchId);

        var items = journal.GetItems(batchId);
        var processed = items.Count(item => item.State != ItemState.Pending);
        var cancelled = false;
        foreach (var item in items.Where(item => item.State == ItemState.Pending))
        {
            if (cancellation.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            Execute(item);
            progress?.Report(new BatchProgress(++processed, items.Count));
        }

        journal.SetBatchState(batchId, cancelled ? BatchState.Interrupted : BatchState.Completed);
        return new BatchRunSummary(batchId, journal.GetBatch(batchId)!.Counts, cancelled);
    }

    private void SkipFailingEntries(long batchId)
    {
        var pending = journal.GetItems(batchId).Where(item => item.State == ItemState.Pending).ToList();
        var reasons = BatchPreflight.Evaluate(pending
            .Select(item => new PreflightEntry(item.Rank, item.GroupId, item.SourcePath, item.TargetPath, item.SourceSize, item.SourceLastWriteUtc))
            .ToList());

        foreach (var item in pending.Where(item => reasons.ContainsKey(item.Rank)))
        {
            journal.SetItemState(item.Id, ItemState.Skipped, reasons[item.Rank]);
        }
    }

    private void Execute(BatchItem item)
    {
        journal.SetItemState(item.Id, ItemState.Running);   // validé avant le déplacement
        try
        {
            moveFile(item.SourcePath, item.TargetPath);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            journal.SetItemState(item.Id, ItemState.Failed, ex.Message);
            return;
        }

        // Le fichier est désormais sous son nouveau nom.
        try
        {
            var instant = item.DecidedDate.UtcDateTime;
            setTimes(item.TargetPath, instant, instant);
            var info = new FileInfo(item.TargetPath);
            journal.SetItemDone(item.Id, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            HandleDatesFailure(item, ex);
        }
    }

    // Les dates n'ont pas pu être appliquées après le déplacement : retour arrière au mieux. L'emplacement du fichier ne se déduit
    // que du résultat du déplacement de retour, jamais de celui de la restauration des dates.
    private void HandleDatesFailure(BatchItem item, Exception datesFailure)
    {
        try
        {
            moveFile(item.TargetPath, item.SourcePath);
        }
        catch (Exception moveBackFailure) when (IsFileSystemFailure(moveBackFailure))
        {
            KeepRenamed(item, datesFailure, moveBackFailure);
            return;
        }

        // Le fichier a retrouvé son ancien nom ; ses dates d'origine ne sont restaurées que si elles ont bougé.
        try
        {
            var current = FileTimestamps.Read(item.SourcePath);
            if (current.CreationUtc != item.SourceCreationUtc || current.LastWriteUtc != item.SourceLastWriteUtc)
            {
                setTimes(item.SourcePath, item.SourceCreationUtc, item.SourceLastWriteUtc);
            }
        }
        catch (Exception restoreFailure) when (IsFileSystemFailure(restoreFailure))
        {
            journal.SetItemState(
                item.Id,
                ItemState.Failed,
                $"{datesFailure.Message} (renommage annulé, dates d'origine non restaurées : {restoreFailure.Message})");
            return;
        }

        journal.SetItemState(item.Id, ItemState.Failed, datesFailure.Message + " (renommage annulé)");
    }

    // Retour arrière impossible : le fichier reste sous son nouveau nom, il est bien renommé, seules ses dates n'ont pas été appliquées.
    // L'élément est donc « fait » (avec un avertissement), pour que l'annulation du lot, qui ne traite que les éléments faits, puisse le restaurer.
    private void KeepRenamed(BatchItem item, Exception datesFailure, Exception moveBackFailure)
    {
        try
        {
            var info = new FileInfo(item.TargetPath);
            journal.SetItemDone(
                item.Id,
                info.Length,
                info.LastWriteTimeUtc,
                $"dates non appliquées : {datesFailure.Message} ; retour arrière impossible : {moveBackFailure.Message}");
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // Le fichier n'est pas non plus sous le nouveau nom (retour arrière interrompu en route) : le message du déplacement dit où il est.
            journal.SetItemState(
                item.Id,
                ItemState.Failed,
                $"{datesFailure.Message} (retour arrière impossible : {moveBackFailure.Message} ; fichier illisible sous {item.TargetPath} : {ex.Message})");
        }
    }

    private static bool IsFileSystemFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;
}
