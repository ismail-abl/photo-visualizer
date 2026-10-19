using PhotoApp.Core.Batches;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Operations;

/// <param name="Skipped">Rangs du plan que les contrôles préalables écartent, avec la raison.</param>
public sealed record PreparedRename(BatchPlan Plan, IReadOnlyList<string> Folders, IReadOnlyDictionary<int, string> Skipped)
{
    public int Renamable => Plan.Items.Count - Skipped.Count;
}

/// <summary>
/// Préparer puis appliquer un lot de renommage sur place : le même enchaînement pour la CLI et l'interface.
/// <see cref="Prepare"/> n'écrit rien ; <see cref="Apply"/> enregistre le lot dans le journal puis l'exécute.
/// </summary>
public sealed class RenameWorkflow(IBatchJournal journal, TimeZoneInfo? zone = null)
{
    public PreparedRename Prepare(IReadOnlyList<MediaResult> results, IReadOnlyList<string> folders, int? maxGroups = null)
    {
        var plan = new BatchPlanner(zone).Plan(results, maxGroups);
        var skipped = BatchPreflight.Evaluate(plan.Items
            .Select(item => new PreflightEntry(item.Rank, item.GroupId, item.SourcePath, item.TargetPath, item.SourceSize, item.SourceLastWriteUtc))
            .ToList());
        return new PreparedRename(plan, folders, skipped);
    }

    public BatchRunSummary Apply(PreparedRename prepared, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        if (prepared.Plan.Items.Count == 0)
        {
            throw new InvalidOperationException("Le plan est vide : rien à renommer.");
        }

        if (journal.FindUnfinished() is { } unfinished)
        {
            throw new InvalidOperationException(
                $"Le lot {unfinished.Id} est interrompu : terminez-le (reprend) ou annulez-le (annule) avant d'en lancer un autre.");
        }

        var batchId = journal.CreateBatch(DateTime.UtcNow, prepared.Folders, prepared.Plan.Items);
        return new InPlaceRenamer(journal).Run(batchId, cancellation, progress);
    }
}
