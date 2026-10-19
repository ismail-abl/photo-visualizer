using System.Globalization;
using PhotoApp.Catalog;
using PhotoApp.Cli.Report;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <param name="Apply">Sans lui, la commande n'est qu'un aperçu : aucun fichier n'est modifié.</param>
/// <param name="AssumeYes">Pas de confirmation interactive (sans effet sans <paramref name="Apply"/>).</param>
/// <param name="Slice">Ne traite que les n premiers groupes du plan (dossiers synchronisés).</param>
public sealed record RenommeRequest(IReadOnlyList<string> Folders, IReadOnlyList<string> Excluded, bool Apply, bool AssumeYes, int? Slice);

/// <summary>
/// Commande « renomme » (spec de phase 2, §7) : analyse, plan, et, avec --applique, renommage sur place journalisé et annulable.
/// Le plan détaillé va dans le dossier reports des données de l'application, jamais dans un dossier analysé.
/// </summary>
public sealed class RenommeCommand(AppDataPaths paths, TextWriter output, TextWriter error, TextReader input, Func<DateTime>? localNow = null)
{
    /// <summary>Au-delà, un dossier synchronisé déclenche l'avertissement sur les gros lots.</summary>
    public const int SyncWarningThreshold = 500;

    private readonly Func<DateTime> _localNow = localNow ?? (() => DateTime.Now);

    /// <param name="executionStarting">
    /// Appelé une seule fois, juste avant l'exécution du lot (donc après la confirmation, jamais pour un aperçu, un abandon
    /// ou un plan vide) : la CLI n'intercepte Ctrl+C qu'à partir de là, car avant, rien n'est encore écrit et l'arrêt immédiat est sans risque.
    /// </param>
    public int Run(RenommeRequest request, CancellationToken cancellation = default, Action? executionStarting = null)
    {
        if (request.Slice is <= 0)
        {
            error.WriteLine("--tranche doit être un entier positif.");
            return ExitCodes.InvalidArguments;
        }

        var folders = request.Folders.Select(folder => Path.GetFullPath(folder)).ToList();
        var missing = folders.FirstOrDefault(folder => !Directory.Exists(folder));
        if (missing is not null)
        {
            error.WriteLine($"Dossier introuvable : {missing}");
            return ExitCodes.InvalidArguments;
        }

        // La garde passe avant toute création de dossier : rien n'est écrit dans un dossier analysé.
        var reportPath = Path.Combine(paths.ReportsFolder, "renomme-" + _localNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
        if (folders.Any(folder => AnalyseCommand.IsInsideAnalysedFolder(reportPath, folder)))
        {
            error.WriteLine($"Le plan ne doit pas être écrit dans un dossier analysé : {reportPath}");
            return ExitCodes.InvalidArguments;
        }

        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        if (journal.FindUnfinished() is { } unfinished)
        {
            error.WriteLine($"Le lot {unfinished.Id} est interrompu : « photoapp reprend {unfinished.Id} » pour le terminer, ou « photoapp annule {unfinished.Id} » pour l'annuler.");
            if (request.Apply)
            {
                return ExitCodes.InvalidArguments;
            }
        }

        var excluded = request.Excluded.Select(folder => Path.GetFullPath(folder)).ToList();
        var analysis = new MediaAnalyzer().Analyze(folders, excluded, new ConsoleProgress(error));
        error.WriteLine();

        var workflow = new RenameWorkflow(journal);
        var prepared = workflow.Prepare(analysis.Results, folders, request.Slice);
        try
        {
            PlanCsvWriter.WriteFile(reportPath, prepared);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Impossible d'écrire le plan {reportPath} : {ex.Message}");
            return ExitCodes.Fatal;
        }

        PrintPlan(prepared, reportPath);
        if (prepared.Renamable == 0)
        {
            output.WriteLine("Rien à renommer.");
            return ExitCodes.Success;
        }

        if (!request.Apply)
        {
            output.WriteLine("Aucune modification : ajoutez --applique pour exécuter ce plan.");
            return ExitCodes.Success;
        }

        if (prepared.Renamable >= SyncWarningThreshold && folders.Any(SyncFolderDetector.IsSynced))
        {
            error.WriteLine(
                $"Attention : dossier synchronisé (Nextcloud) et lot de {prepared.Renamable} fichiers. " +
                "Pour ménager la synchronisation, --tranche <n> traite le plan par tranches de n groupes.");
        }

        if (!request.AssumeYes && !Confirmation.Ask($"Renommer {prepared.Renamable} fichier(s) sur place ?", input, output))
        {
            output.WriteLine("Abandon, aucun fichier modifié.");
            return ExitCodes.Success;
        }

        executionStarting?.Invoke();
        BatchRunSummary summary;
        try
        {
            summary = workflow.Apply(prepared, cancellation, new BatchConsoleProgress(error, "Renommage"));
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

    private void PrintPlan(PreparedRename prepared, string reportPath)
    {
        output.WriteLine($"À renommer : {prepared.Renamable} fichier(s) ({prepared.Plan.Summary.Groups} groupe(s))");
        output.WriteLine($"Déjà conformes : {prepared.Plan.Summary.AlreadyConforming}");
        output.WriteLine($"Écartés par les contrôles : {prepared.Skipped.Count}");
        output.WriteLine($"Conversion différée (HEIC, HEIF, AVIF) : {prepared.Plan.Summary.DeferredConversion}");
        output.WriteLine($"Plan détaillé : {reportPath}");
    }
}
