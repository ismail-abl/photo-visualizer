namespace PhotoApp.Core.Batches;

public enum QuarantineState
{
    Held,
    Restored,
    Purged
}

public sealed record QuarantineEntry(long Id, DateTime CreatedUtc, string OriginalPath, string StoredPath, long? BatchId, QuarantineState State);

/// <summary>Où est chaque fichier mis en quarantaine (cadre §7 : « chaque emplacement est enregistré dans la base »).</summary>
public interface IQuarantineLog
{
    long Add(DateTime createdUtc, string originalPath, string storedPath, long? batchId);

    QuarantineEntry? Get(long id);

    /// <summary>Les entrées, de la plus ancienne à la plus récente ; toutes si <paramref name="state"/> est nul.</summary>
    IReadOnlyList<QuarantineEntry> List(QuarantineState? state = null);

    void SetState(long id, QuarantineState state);
}
