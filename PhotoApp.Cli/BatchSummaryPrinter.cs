using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Textes de fin de lot et libellés d'états, partagés par les commandes.</summary>
public static class BatchSummaryPrinter
{
    private const int MaxListed = 20;

    public static void PrintRun(TextWriter output, IBatchJournal journal, BatchRunSummary summary)
    {
        var counts = summary.Counts;
        output.WriteLine($"Lot {summary.BatchId} : {counts.Done} renommé(s), {counts.Failed} en échec, {counts.Skipped} écarté(s).");
        PrintProblems(output, journal, summary.BatchId, ItemState.Failed, ItemState.Skipped);
        PrintWarnings(output, journal, summary.BatchId);
        if (summary.Cancelled)
        {
            output.WriteLine($"Lot interrompu : « photoapp reprend {summary.BatchId} » pour le terminer, ou « photoapp annule {summary.BatchId} » pour l'annuler.");
        }
        else
        {
            output.WriteLine($"Détail : photoapp journal --lot {summary.BatchId} ; pour revenir en arrière : photoapp annule {summary.BatchId}");
        }
    }

    /// <summary>Les fichiers du lot dans les états demandés, avec leur message, au plus 20 lignes.</summary>
    public static void PrintProblems(TextWriter output, IBatchJournal journal, long batchId, params ItemState[] states)
    {
        var problems = journal.GetItems(batchId).Where(item => states.Contains(item.State)).ToList();
        PrintLimited(output, batchId, problems, item => $"{StateLabel(item.State)} : {item.SourcePath} — {item.Error}");
    }

    /// <summary>
    /// Les fichiers faits (ou annulés) dont le message n'est pas vide, au plus 20 lignes : ils sont bien traités, donc comptés
    /// parmi les réussites, mais une réserve les accompagne (par exemple des dates qui n'ont pas pu être appliquées).
    /// N'écrit rien s'il n'y en a aucun.
    /// </summary>
    public static void PrintWarnings(TextWriter output, IBatchJournal journal, long batchId)
    {
        var warnings = journal.GetItems(batchId)
            .Where(item => (item.State is ItemState.Done or ItemState.Undone) && !string.IsNullOrEmpty(item.Error))
            .ToList();
        if (warnings.Count == 0)
        {
            return;
        }

        output.WriteLine($"{warnings.Count} fichier(s) traité(s) avec avertissement :");
        PrintLimited(output, batchId, warnings, item => $"avertissement : {item.SourcePath} — {item.Error}");
    }

    private static void PrintLimited(TextWriter output, long batchId, IReadOnlyList<BatchItem> items, Func<BatchItem, string> line)
    {
        foreach (var item in items.Take(MaxListed))
        {
            output.WriteLine("  " + line(item));
        }

        if (items.Count > MaxListed)
        {
            output.WriteLine($"  … et {items.Count - MaxListed} autre(s) : photoapp journal --lot {batchId}");
        }
    }

    public static string StateLabel(ItemState state) => state switch
    {
        ItemState.Pending => "en attente",
        ItemState.Running => "en cours",
        ItemState.Done => "fait",
        ItemState.Failed => "échec",
        ItemState.Skipped => "écarté",
        ItemState.Undone => "annulé",
        ItemState.UndoFailed => "annulation impossible",
        _ => state.ToString()
    };

    public static string BatchStateLabel(BatchState state) => state switch
    {
        BatchState.Planned => "Planifié",
        BatchState.Running => "En cours",
        BatchState.Completed => "Terminé",
        BatchState.Interrupted => "Interrompu",
        BatchState.Undone => "Annulé",
        _ => state.ToString()
    };
}
