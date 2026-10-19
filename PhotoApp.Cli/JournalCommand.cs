using System.Globalization;
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;

namespace PhotoApp.Cli;

/// <summary>Commande « journal » : la liste des lots, ou le détail fichier par fichier d'un lot.</summary>
public sealed class JournalCommand(AppDataPaths paths, TextWriter output, TextWriter error)
{
    public int Run(long? batchId)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        return batchId is { } id ? PrintDetail(journal, id) : PrintList(journal);
    }

    private int PrintList(IBatchJournal journal)
    {
        var batches = journal.ListBatches();
        if (batches.Count == 0)
        {
            output.WriteLine("Aucun lot dans le journal.");
            return ExitCodes.Success;
        }

        foreach (var batch in batches)
        {
            var counts = batch.Counts;
            output.WriteLine(
                $"{batch.Id,4}  {Local(batch.CreatedUtc)}  {BatchSummaryPrinter.BatchStateLabel(batch.State),-10}  " +
                $"{counts.Done} fait(s), {counts.Failed} échec(s), {counts.Skipped} écarté(s)  {string.Join(" ; ", batch.Folders)}");
        }

        if (journal.FindUnfinished() is { } unfinished)
        {
            output.WriteLine($"Lot {unfinished.Id} interrompu : « photoapp reprend {unfinished.Id} » pour le terminer, ou « photoapp annule {unfinished.Id} » pour l'annuler.");
        }

        return ExitCodes.Success;
    }

    private int PrintDetail(IBatchJournal journal, long batchId)
    {
        var batch = journal.GetBatch(batchId);
        if (batch is null)
        {
            error.WriteLine($"Lot {batchId} introuvable.");
            return ExitCodes.InvalidArguments;
        }

        output.WriteLine($"Lot {batch.Id} — {BatchSummaryPrinter.BatchStateLabel(batch.State)} — {Local(batch.CreatedUtc)}");
        output.WriteLine($"Dossiers : {string.Join(" ; ", batch.Folders)}");
        foreach (var item in journal.GetItems(batchId))
        {
            output.WriteLine($"{item.Rank,5}  {BatchSummaryPrinter.StateLabel(item.State),-22}  {item.SourcePath} → {item.TargetPath}");
            if (!string.IsNullOrEmpty(item.Error))
            {
                output.WriteLine($"       {item.Error}");
            }
        }

        return ExitCodes.Success;
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
