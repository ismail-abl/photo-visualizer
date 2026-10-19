using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <param name="Completed">Éléments dont le renommage avait eu lieu : marqués faits.</param>
/// <param name="RolledBack">Éléments dont le renommage n'avait pas eu lieu : remis en attente.</param>
/// <param name="Anomalies">Éléments incohérents (deux noms présents, ou aucun) : en échec, rien n'est écrasé.</param>
public sealed record RecoveryReport(int Completed, int RolledBack, int Anomalies);

/// <summary>
/// Résout les éléments laissés « en cours » par un lot interrompu brutalement, d'après l'état réel du disque (spec de phase 2, §5.4).
/// </summary>
public sealed class BatchRecovery(IBatchJournal journal)
{
    public RecoveryReport Resolve(long batchId)
    {
        int completed = 0, rolledBack = 0, anomalies = 0;
        foreach (var item in journal.GetItems(batchId).Where(item => item.State == ItemState.Running))
        {
            var (sourcePresent, targetPresent) = Presence(item);
            if (!sourcePresent && targetPresent)
            {
                // Le renommage avait eu lieu : on réapplique les dates (idempotent) et on note l'état du fichier.
                var instant = item.DecidedDate.UtcDateTime;
                FileTimestamps.Set(item.TargetPath, instant, instant);
                var info = new FileInfo(item.TargetPath);
                journal.SetItemDone(item.Id, info.Length, info.LastWriteTimeUtc);
                completed++;
            }
            else if (sourcePresent && !targetPresent)
            {
                journal.SetItemState(item.Id, ItemState.Pending);
                rolledBack++;
            }
            else if (sourcePresent)
            {
                journal.SetItemState(item.Id, ItemState.Failed, $"Les deux noms existent : {Path.GetFileName(item.SourcePath)} et {Path.GetFileName(item.TargetPath)}");
                anomalies++;
            }
            else
            {
                journal.SetItemState(item.Id, ItemState.Failed, "Fichier introuvable");
                anomalies++;
            }
        }

        return new RecoveryReport(completed, rolledBack, anomalies);
    }

    private static (bool Source, bool Target) Presence(BatchItem item)
    {
        if (string.Equals(item.SourcePath, item.TargetPath, StringComparison.OrdinalIgnoreCase)
            && ActualName(item.TargetPath) is { } actual)
        {
            // Changement de casse seul : un seul fichier existe, c'est la casse réelle de son nom qui dit où en est le renommage.
            return string.Equals(actual, Path.GetFileName(item.TargetPath), StringComparison.Ordinal) ? (false, true) : (true, false);
        }

        return (File.Exists(item.SourcePath), File.Exists(item.TargetPath));
    }

    // Nom tel qu'il est écrit dans le dossier (casse comprise), ou null si aucun fichier ne correspond.
    private static string? ActualName(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFileSystemEntries(folder, Path.GetFileName(path)).Select(Path.GetFileName).FirstOrDefault();
    }
}
