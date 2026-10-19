namespace PhotoApp.Core.Batches;

/// <summary>Le journal des lots (spec de phase 2, §6). Chaque changement d'état est durable avant que l'action suivante commence.</summary>
public interface IBatchJournal
{
    /// <summary>Enregistre un lot à l'état <see cref="BatchState.Planned"/>, tous ses éléments <see cref="ItemState.Pending"/>.</summary>
    long CreateBatch(DateTime createdUtc, IReadOnlyList<string> folders, IReadOnlyList<BatchItemDraft> items);

    BatchInfo? GetBatch(long batchId);

    /// <summary>Tous les lots, du plus ancien au plus récent.</summary>
    IReadOnlyList<BatchInfo> ListBatches();

    /// <summary>Le lot le plus récent à l'état <see cref="BatchState.Running"/> ou <see cref="BatchState.Interrupted"/>, s'il existe.</summary>
    BatchInfo? FindUnfinished();

    /// <summary>Le lot terminé le plus récent qui a encore des éléments à restaurer (faits ou en échec d'annulation).</summary>
    BatchInfo? FindLastUndoable();

    /// <summary>Les éléments d'un lot, dans l'ordre d'exécution.</summary>
    IReadOnlyList<BatchItem> GetItems(long batchId);

    void SetBatchState(long batchId, BatchState state);

    void SetItemState(long itemId, ItemState state, string? error = null);

    /// <summary>Marque l'élément fait et enregistre l'état du fichier renommé ; <paramref name="warning"/> : avertissement éventuel.</summary>
    void SetItemDone(long itemId, long doneSize, DateTime doneLastWriteUtc, string? warning = null);

    /// <summary>Un seul exécuteur ou annulateur à la fois. Lève <see cref="BatchLockedException"/> si un autre traitement est en cours.</summary>
    IDisposable AcquireExecutionLock();
}

public sealed class BatchLockedException(string message, Exception? inner = null) : Exception(message, inner);
