using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

public sealed record BatchProgress(int Processed, int Total);

public sealed record BatchRunSummary(long BatchId, BatchCounts Counts, bool Cancelled);

/// <summary>
/// Exécute un lot : renommage sur place, un seul déplacement par fichier, journal écrit avant chaque action
/// (spec de phase 2, §5.3). Un échec sur un fichier n'arrête pas le lot ; l'arrêt demandé termine le fichier en cours.
/// </summary>
public sealed class InPlaceRenamer(IBatchJournal journal)
{
    public BatchRunSummary Run(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        using var executionLock = journal.AcquireExecutionLock();

        var batch = journal.GetBatch(batchId) ?? throw new InvalidOperationException($"Lot {batchId} introuvable.");
        if (batch.State is BatchState.Completed or BatchState.Undone)
        {
            throw new InvalidOperationException($"Le lot {batchId} est déjà terminé.");
        }

        journal.SetBatchState(batchId, BatchState.Running);
        new BatchRecovery(journal).Resolve(batchId);
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
        var moved = false;
        try
        {
            FileMoves.Move(item.SourcePath, item.TargetPath);
            moved = true;
            var instant = item.DecidedDate.UtcDateTime;
            FileTimestamps.Set(item.TargetPath, instant, instant);
            var info = new FileInfo(item.TargetPath);
            journal.SetItemDone(item.Id, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var message = moved ? ex.Message + RollBack(item) : ex.Message;
            journal.SetItemState(item.Id, ItemState.Failed, message);
        }
    }

    // Retour arrière au mieux quand les dates n'ont pas pu être appliquées : le fichier retrouve son nom et ses dates d'origine.
    private static string RollBack(BatchItem item)
    {
        try
        {
            FileMoves.Move(item.TargetPath, item.SourcePath);
            FileTimestamps.Set(item.SourcePath, item.SourceCreationUtc, item.SourceLastWriteUtc);
            return " (renommage annulé)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $" (retour arrière impossible, le fichier est sous {item.TargetPath} : {ex.Message})";
        }
    }
}
