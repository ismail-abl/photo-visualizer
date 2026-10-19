using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Commande « reprend » : termine un lot interrompu (résout d'abord les fichiers laissés « en cours » d'après le disque).</summary>
public sealed class ReprendCommand(AppDataPaths paths, TextWriter output, TextWriter error)
{
    public int Run(long batchId, CancellationToken cancellation = default)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        var batch = journal.GetBatch(batchId);
        if (batch is null)
        {
            error.WriteLine($"Lot {batchId} introuvable.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State is BatchState.Completed or BatchState.Undone)
        {
            error.WriteLine($"Le lot {batchId} est déjà terminé ({BatchSummaryPrinter.BatchStateLabel(batch.State)}).");
            return ExitCodes.InvalidArguments;
        }

        BatchRunSummary summary;
        try
        {
            summary = new InPlaceRenamer(journal).Run(batchId, cancellation, new BatchConsoleProgress(error, "Renommage"));
        }
        catch (BatchLockedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Fatal;
        }

        error.WriteLine();
        BatchSummaryPrinter.PrintRun(output, journal, summary);
        return summary.Cancelled || summary.Counts.Failed > 0 ? ExitCodes.BatchIssues : ExitCodes.Success;
    }
}
