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

    // Un média retenu avec la date que prendront ses fichiers.
    private sealed record Candidate(MediaResult Result, DateCandidate Date);

    public BatchPlan Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null)
    {
        var alreadyConforming = 0;
        var candidates = new List<Candidate>();
        var allByPath = new Dictionary<string, MediaResult>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            allByPath.TryAdd(result.Facts.File.FullPath, result);
        }

        foreach (var result in results)
        {
            if (result.ProposedName is null || DateOf(result, allByPath) is not { } date)
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(result.Facts.File.FullPath), result.ProposedName, StringComparison.Ordinal))
            {
                alreadyConforming++;
            }
            else
            {
                candidates.Add(new Candidate(result, date));
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
            var members = groups[index].OrderBy(member => member.Result.Kind).ToList();   // la photo avant sa vidéo
            foreach (var (media, date) in members)
            {
                var file = media.Facts.File;
                var folder = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
                var decided = date.ToInstant(_zone);
                var role = members.Count > 1 && media.Kind == MediaKind.Video ? ItemRole.LivePartner : ItemRole.Media;

                items.Add(new BatchItemDraft(
                    items.Count, index + 1, role, file.FullPath, Path.Combine(folder, media.ProposedName!), decided,
                    file.SizeBytes, file.LastWriteUtc, CreationOf(file.FullPath, file.LastWriteUtc)));
                if (DeferredExtensions.Contains(Path.GetExtension(media.ProposedName!)))
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

                    var newName = SidecarNames.Rename(file.FileName, media.ProposedName!, Path.GetFileName(sidecar));
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

    // La date d'un média : sa propre décision si elle est applicable. Sinon, une vidéo Live Photo à laquelle le moteur a quand même
    // proposé le nom de sa photo (phase 1) prend la date de cette photo, pour que les deux moitiés restent ensemble.
    private static DateCandidate? DateOf(MediaResult result, Dictionary<string, MediaResult> allByPath)
    {
        if (result.Decision is { IsApplicable: true, Date: { } own })
        {
            return own;
        }

        if (result.LivePhotoPartner is { } partner
            && allByPath.TryGetValue(PartnerPath(result, partner), out var mate)
            && mate.Decision is { IsApplicable: true, Date: { } followed })
        {
            return followed;
        }

        return null;
    }

    private static string PartnerPath(MediaResult result, string partner) =>
        Path.Combine(Path.GetDirectoryName(result.Facts.File.FullPath) ?? string.Empty, partner);

    // Ordre stable (chemin), un groupe par média, les deux moitiés d'une Live Photo ensemble.
    private static List<List<Candidate>> BuildGroups(List<Candidate> candidates)
    {
        var ordered = candidates.OrderBy(candidate => candidate.Result.Facts.File.FullPath, StringComparer.Ordinal).ToList();
        var byPath = ordered.ToDictionary(candidate => candidate.Result.Facts.File.FullPath, StringComparer.Ordinal);
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<List<Candidate>>();

        foreach (var candidate in ordered)
        {
            var path = candidate.Result.Facts.File.FullPath;
            if (!grouped.Add(path))
            {
                continue;
            }

            var group = new List<Candidate> { candidate };
            if (candidate.Result.LivePhotoPartner is { } partner)
            {
                var partnerPath = PartnerPath(candidate.Result, partner);
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
