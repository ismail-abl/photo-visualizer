namespace PhotoApp.Core.Batches;

/// <summary>États d'un lot (spec de phase 2, §5.3).</summary>
public enum BatchState
{
    Planned,
    Running,
    Completed,
    Interrupted,
    Undone
}

/// <summary>États d'un fichier du lot. <see cref="UndoFailed"/> : l'annulation n'a pas pu le restaurer, elle peut être retentée.</summary>
public enum ItemState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped,
    Undone,
    UndoFailed
}

public enum ItemRole
{
    Media,
    LivePartner,
    Sidecar
}

/// <summary>Un fichier à renommer, tel qu'il est figé au plan. Les dates sont en UTC.</summary>
/// <param name="Rank">Rang dans le lot, à partir de 0 : l'ordre d'exécution.</param>
/// <param name="GroupId">Média, partenaire Live Photo et sidecars d'un même groupe : tout ou rien au plan.</param>
/// <param name="DecidedDate">Instant que prendront les dates de création et de modification.</param>
public sealed record BatchItemDraft(
    int Rank,
    int GroupId,
    ItemRole Role,
    string SourcePath,
    string TargetPath,
    DateTimeOffset DecidedDate,
    long SourceSize,
    DateTime SourceLastWriteUtc,
    DateTime SourceCreationUtc);

/// <param name="Error">Message d'échec, ou avertissement d'un élément fait.</param>
/// <param name="DoneSize">Taille du fichier après renommage : sert à détecter une modification avant l'annulation.</param>
/// <param name="DoneLastWriteUtc">Date de modification après renommage (idem).</param>
public sealed record BatchItem(
    long Id,
    long BatchId,
    int Rank,
    int GroupId,
    ItemRole Role,
    string SourcePath,
    string TargetPath,
    ItemState State,
    string? Error,
    DateTimeOffset DecidedDate,
    long SourceSize,
    DateTime SourceLastWriteUtc,
    DateTime SourceCreationUtc,
    long? DoneSize,
    DateTime? DoneLastWriteUtc);

public sealed record BatchCounts(int Total, int Pending, int Running, int Done, int Failed, int Skipped, int Undone, int UndoFailed);

public sealed record BatchInfo(long Id, DateTime CreatedUtc, IReadOnlyList<string> Folders, BatchState State, BatchCounts Counts);
