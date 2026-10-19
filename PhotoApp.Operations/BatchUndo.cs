using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <param name="Completed">Plus aucun élément à restaurer : le lot est annulé.</param>
public sealed record UndoSummary(long BatchId, int Restored, int Failed, bool Completed);

/// <summary>
/// Annule un lot (spec de phase 2, §5.5) : chaque fichier fait retrouve son nom et ses dates d'origine, dans l'ordre inverse.
/// Un fichier modifié depuis le lot, ou dont le nom d'origine est occupé, est laissé en place et signalé ; jamais écrasé.
/// </summary>
public sealed class BatchUndo
{
    private readonly IBatchJournal journal;
    private readonly Action<string, DateTime, DateTime> setTimes;

    public BatchUndo(IBatchJournal journal)
        : this(journal, FileTimestamps.Set)
    {
    }

    // Réglage des dates injectable : seul moyen de provoquer, dans les tests, l'échec tardif d'un système de fichiers.
    internal BatchUndo(IBatchJournal journal, Action<string, DateTime, DateTime> setTimes)
    {
        this.journal = journal;
        this.setTimes = setTimes;
    }

    public UndoSummary Undo(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        using var executionLock = journal.AcquireExecutionLock();

        var batch = journal.GetBatch(batchId) ?? throw new InvalidOperationException($"Lot {batchId} introuvable.");
        if (batch.State == BatchState.Undone)
        {
            throw new InvalidOperationException($"Le lot {batchId} est déjà annulé.");
        }

        if (batch.State == BatchState.Planned)
        {
            throw new InvalidOperationException($"Le lot {batchId} n'a pas été exécuté.");
        }

        // Un lot tué brutalement a peut-être un fichier déjà renommé mais encore « en cours ».
        new BatchRecovery(journal).Resolve(batchId);

        var todo = journal.GetItems(batchId)
            .Where(item => item.State is ItemState.Done or ItemState.UndoFailed)
            .OrderByDescending(item => item.Rank)
            .ToList();

        int restored = 0, failed = 0, processed = 0;
        foreach (var item in todo)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            var outcome = Restore(item);
            if (outcome.Restored)
            {
                journal.SetItemState(item.Id, ItemState.Undone, outcome.Message);
                restored++;
            }
            else
            {
                journal.SetItemState(item.Id, ItemState.UndoFailed, outcome.Message);
                failed++;
            }

            progress?.Report(new BatchProgress(++processed, todo.Count));
        }

        var remaining = journal.GetItems(batchId).Any(item => item.State is ItemState.Done or ItemState.UndoFailed);
        if (!remaining)
        {
            foreach (var pending in journal.GetItems(batchId).Where(item => item.State == ItemState.Pending))
            {
                journal.SetItemState(pending.Id, ItemState.Skipped, "Lot annulé avant exécution");
            }

            journal.SetBatchState(batchId, BatchState.Undone);
        }

        return new UndoSummary(batchId, restored, failed, !remaining);
    }

    /// <param name="Restored">Le fichier a retrouvé son nom d'origine.</param>
    /// <param name="Message">Restauré : avertissement éventuel (dates non restaurées). Laissé en place : la raison.</param>
    private readonly record struct Restoration(bool Restored, string? Message);

    private Restoration Restore(BatchItem item)
    {
        var target = new FileInfo(item.TargetPath);
        if (!target.Exists)
        {
            return new Restoration(false, "Fichier introuvable sous son nouveau nom");
        }

        if (target.Length != item.DoneSize || target.LastWriteTimeUtc != item.DoneLastWriteUtc)
        {
            return new Restoration(false, "Fichier modifié depuis le lot : laissé en place");
        }

        var caseOnly = string.Equals(item.SourcePath, item.TargetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(item.SourcePath) || Directory.Exists(item.SourcePath)))
        {
            return new Restoration(false, "Le nom d'origine est occupé : laissé en place");
        }

        // L'emplacement du fichier ne se déduit que du résultat du déplacement, jamais de celui de la restauration des dates.
        try
        {
            FileMoves.Move(item.TargetPath, item.SourcePath);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return new Restoration(false, ex.Message);
        }

        // Le fichier a retrouvé son nom d'origine : s'il garde les dates du lot, l'élément est tout de même annulé, avec un avertissement.
        try
        {
            setTimes(item.SourcePath, item.SourceCreationUtc, item.SourceLastWriteUtc);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return new Restoration(true, $"nom restauré, dates d'origine non restaurées : {ex.Message}");
        }

        return new Restoration(true, null);
    }

    private static bool IsFileSystemFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;
}
