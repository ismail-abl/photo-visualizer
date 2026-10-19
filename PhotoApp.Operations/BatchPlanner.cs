using PhotoApp.Core.Batches;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Operations;

/// <param name="AlreadyConforming">Médias dont le nom actuel est déjà le nom proposé : rien à faire.</param>
/// <param name="Files">Fichiers du plan (médias, partenaires Live Photo et sidecars).</param>
/// <param name="DeferredConversion">Fichiers HEIC, HEIF ou AVIF : renommés tels quels, conversion en phase 3.</param>
public sealed record PlanSummary(int AlreadyConforming, int Files, int Groups, int DeferredConversion);

public sealed record BatchPlan(IReadOnlyList<BatchItemDraft> Items, PlanSummary Summary);

/// <summary>
/// Construit le plan figé d'un lot (spec de phase 2, §5.1) à partir de l'analyse de phase 1 : seuls les médias Sûre et Probable
/// qui ont un nom proposé différent du nom actuel. Un média, son partenaire Live Photo et ses sidecars forment un groupe.
/// </summary>
public sealed class BatchPlanner(TimeZoneInfo? zone = null)
{
    private static readonly HashSet<string> DeferredExtensions = new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif", ".avif" };

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public BatchPlan Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null)
    {
        var alreadyConforming = 0;
        var candidates = new List<MediaResult>();
        foreach (var result in results)
        {
            if (result.Decision is not { IsApplicable: true, Date: not null } || result.ProposedName is null)
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(result.Facts.File.FullPath), result.ProposedName, StringComparison.Ordinal))
            {
                alreadyConforming++;
            }
            else
            {
                candidates.Add(result);
            }
        }

        var groups = BuildGroups(candidates);
        if (maxGroups is { } limit)
        {
            groups = groups.Take(limit).ToList();
        }

        var items = new List<BatchItemDraft>();
        var claimedSidecars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deferred = 0;
        for (var index = 0; index < groups.Count; index++)
        {
            var members = groups[index].OrderBy(member => member.Kind).ToList();   // la photo avant sa vidéo
            foreach (var member in members)
            {
                var file = member.Facts.File;
                var folder = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
                var decided = member.Decision.Date!.ToInstant(_zone);
                var role = members.Count > 1 && member.Kind == MediaKind.Video ? ItemRole.LivePartner : ItemRole.Media;

                items.Add(new BatchItemDraft(
                    items.Count, index + 1, role, file.FullPath, Path.Combine(folder, member.ProposedName!), decided,
                    file.SizeBytes, file.LastWriteUtc, CreationOf(file.FullPath, file.LastWriteUtc)));
                if (DeferredExtensions.Contains(Path.GetExtension(member.ProposedName!)))
                {
                    deferred++;
                }

                foreach (var sidecar in file.Sidecars)
                {
                    // Un sidecar .aae vaut pour les deux moitiés d'une Live Photo : le premier média qui le réclame le garde.
                    if (!claimedSidecars.Add(sidecar))
                    {
                        continue;
                    }

                    var newName = SidecarNames.Rename(file.FileName, member.ProposedName!, Path.GetFileName(sidecar));
                    var info = new FileInfo(sidecar);
                    if (newName is null || !info.Exists)
                    {
                        continue;
                    }

                    items.Add(new BatchItemDraft(
                        items.Count, index + 1, ItemRole.Sidecar, sidecar, Path.Combine(Path.GetDirectoryName(sidecar) ?? folder, newName), decided,
                        info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc));
                }
            }
        }

        return new BatchPlan(items, new PlanSummary(alreadyConforming, items.Count, groups.Count, deferred));
    }

    // Ordre stable (chemin), un groupe par média, les deux moitiés d'une Live Photo ensemble.
    private static List<List<MediaResult>> BuildGroups(List<MediaResult> candidates)
    {
        var ordered = candidates.OrderBy(result => result.Facts.File.FullPath, StringComparer.Ordinal).ToList();
        var byPath = ordered.ToDictionary(result => result.Facts.File.FullPath, StringComparer.Ordinal);
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<List<MediaResult>>();

        foreach (var result in ordered)
        {
            var path = result.Facts.File.FullPath;
            if (!grouped.Add(path))
            {
                continue;
            }

            var group = new List<MediaResult> { result };
            if (result.LivePhotoPartner is { } partner)
            {
                var partnerPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, partner);
                if (byPath.TryGetValue(partnerPath, out var mate) && grouped.Add(partnerPath))
                {
                    group.Add(mate);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static DateTime CreationOf(string path, DateTime fallbackUtc)
    {
        var info = new FileInfo(path);
        return info.Exists ? info.CreationTimeUtc : fallbackUtc;
    }
}
