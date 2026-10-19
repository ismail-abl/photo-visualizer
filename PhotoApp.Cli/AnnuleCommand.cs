using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Commande « annule » : restaure les noms et les dates d'un lot (le dernier lot terminé si aucun n'est indiqué).</summary>
public sealed class AnnuleCommand(AppDataPaths paths, TextWriter output, TextWriter error, TextReader input)
{
    /// <param name="executionStarting">
    /// Appelé une seule fois, juste avant la restauration (donc après la confirmation, jamais en cas d'abandon ou d'argument invalide) :
    /// la CLI n'intercepte Ctrl+C qu'à partir de là, comme pour « renomme ».
    /// </param>
    public int Run(long? batchId, bool assumeYes, CancellationToken cancellation = default, Action? executionStarting = null)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));

        // Sans identifiant, ne pas annuler un lot ancien derrière un lot interrompu : le choix doit être explicite.
        if (batchId is null && journal.FindUnfinished() is { } unfinished)
        {
            error.WriteLine(
                $"Le lot {unfinished.Id} est interrompu : indiquez le lot à annuler (« photoapp annule {unfinished.Id} »), " +
                $"ou terminez-le (« photoapp reprend {unfinished.Id} »).");
            return ExitCodes.InvalidArguments;
        }

        var batch = batchId is { } id ? journal.GetBatch(id) : journal.FindLastUndoable();
        if (batch is null)
        {
            error.WriteLine(batchId is { } missing ? $"Lot {missing} introuvable." : "Aucun lot à annuler.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State == BatchState.Undone)
        {
            error.WriteLine($"Le lot {batch.Id} est déjà annulé.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State == BatchState.Planned)
        {
            error.WriteLine($"Le lot {batch.Id} n'a pas été exécuté.");
            return ExitCodes.InvalidArguments;
        }

        var toRestore = batch.Counts.Done + batch.Counts.UndoFailed + batch.Counts.Running;
        if (!assumeYes && !Confirmation.Ask($"Annuler le lot {batch.Id} ({toRestore} fichier(s) à restaurer) ?", input, output))
        {
            output.WriteLine("Abandon, aucun fichier modifié.");
            return ExitCodes.Success;
        }

        executionStarting?.Invoke();
        UndoSummary summary;
        try
        {
            summary = new BatchUndo(journal).Undo(batch.Id, cancellation, new BatchConsoleProgress(error, "Annulation"));
        }
        catch (BatchLockedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Fatal;
        }

        error.WriteLine();
        output.WriteLine($"Lot {batch.Id} : {summary.Restored} restauré(s), {summary.Failed} non restauré(s).");
        BatchSummaryPrinter.PrintProblems(output, journal, batch.Id, ItemState.UndoFailed);
        BatchSummaryPrinter.PrintWarnings(output, journal, batch.Id);
        output.WriteLine(summary.Completed
            ? "Lot annulé."
            : $"Lot partiellement annulé : relancez « photoapp annule {batch.Id} » une fois les cas ci-dessus réglés.");
        return summary.Completed ? ExitCodes.Success : ExitCodes.BatchIssues;
    }
}
