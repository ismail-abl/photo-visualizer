using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <param name="TargetExtension">Extension normalisée du vrai format, avec le point.</param>
/// <param name="Counter">Compteur lu dans le nom (WAnnnn…) : ordre des fichiers d'une même seconde ou d'un même jour.</param>
public sealed record NamingInput(string FullPath, MediaKind Kind, string TargetExtension, DateDecision Decision, long? Counter, string? ContentIdentifier);

/// <param name="LivePhotoPartner">Nom de fichier actuel de l'autre moitié de la Live Photo.</param>
public sealed record NamingResult(string? ProposedName, string? LivePhotoPartner);

/// <summary>
/// Propose un nom par média Sûr ou Probable, dossier par dossier (spec de phase 1, §7). Rien n'est renommé ici.
/// </summary>
public sealed class MediaNamer
{
    private static readonly Regex NormalizedStem = new(
        @"^(?<base>(?:IMG|VID)-\d{8}-\d{6})(?:-(?<number>\d{2}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IContentHasher _hasher;

    public MediaNamer(IContentHasher hasher)
    {
        _hasher = hasher;
    }

    public IReadOnlyList<NamingResult> Name(IReadOnlyList<NamingInput> inputs)
    {
        var results = Enumerable.Repeat(new NamingResult(null, null), inputs.Count).ToArray();
        var folders = Enumerable.Range(0, inputs.Count)
            .GroupBy(index => Path.GetDirectoryName(inputs[index].FullPath) ?? string.Empty, StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            NameFolder(inputs, folder.ToList(), results);
        }

        return results;
    }

    private void NameFolder(IReadOnlyList<NamingInput> inputs, List<int> indexes, NamingResult[] results)
    {
        var livePairs = PairLivePhotos(inputs, indexes);
        var allocator = new NameAllocator();
        var toAllocate = new List<int>();

        // Les noms déjà conformes sont réservés d'abord : ils ne sont jamais renumérotés.
        foreach (var index in indexes)
        {
            if (livePairs.ContainsKey(index) || inputs[index].Decision is not { IsApplicable: true, Date: { } date })
            {
                continue;
            }

            var current = Path.GetFileName(inputs[index].FullPath);
            if (IsAlreadyNormalized(inputs[index], date) && allocator.TryReserve(current))
            {
                results[index] = new NamingResult(current, null);
            }
            else
            {
                toAllocate.Add(index);
            }
        }

        foreach (var index in InRealOrder(inputs, toAllocate))
        {
            var input = inputs[index];
            results[index] = new NamingResult(allocator.Allocate(input.Decision.Date!, input.TargetExtension, Prefix(input)), null);
        }

        // Live Photo : la vidéo prend le nom de base de sa photo, avec le préfixe IMG.
        foreach (var (video, photo) in livePairs)
        {
            var photoName = results[photo].ProposedName;
            var videoName = photoName is null ? null : Path.GetFileNameWithoutExtension(photoName) + inputs[video].TargetExtension;
            results[video] = new NamingResult(videoName, Path.GetFileName(inputs[photo].FullPath));
            results[photo] = results[photo] with { LivePhotoPartner = Path.GetFileName(inputs[video].FullPath) };
        }
    }

    // Ordre réel : heure (sous-secondes comprises), compteur du nom, puis empreinte du contenu pour les égalités restantes.
    private IEnumerable<int> InRealOrder(IReadOnlyList<NamingInput> inputs, List<int> indexes)
    {
        var groups = indexes
            .GroupBy(index => (inputs[index].Decision.Date!.WallClock, inputs[index].Counter))
            .OrderBy(group => group.Key.WallClock)
            .ThenBy(group => group.Key.Counter ?? long.MaxValue);

        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                yield return members[0];
                continue;
            }

            var hashes = members.ToDictionary(index => index, index => _hasher.Hash(inputs[index].FullPath));
            foreach (var index in members
                         .OrderBy(index => hashes[index], StringComparer.Ordinal)
                         .ThenBy(index => inputs[index].FullPath, StringComparer.Ordinal))
            {
                yield return index;
            }
        }
    }

    private static bool IsAlreadyNormalized(NamingInput input, DateCandidate date)
    {
        var fileName = Path.GetFileName(input.FullPath);
        if (!string.Equals(Path.GetExtension(fileName), input.TargetExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var match = NormalizedStem.Match(Path.GetFileNameWithoutExtension(fileName));
        if (!match.Success || (!date.HasTime && !match.Groups["number"].Success))
        {
            return false;
        }

        return match.Groups["base"].Value == NormalizedFileNameBuilder.BaseName(date, Prefix(input));
    }

    // Vidéo → photo, par ContentIdentifier Apple, sinon par le même nom de base, dans le même dossier.
    private static Dictionary<int, int> PairLivePhotos(IReadOnlyList<NamingInput> inputs, List<int> indexes)
    {
        var pairs = new Dictionary<int, int>();
        var photos = indexes.Where(index => inputs[index].Kind == MediaKind.Photo).ToList();
        var videos = indexes.Where(index => inputs[index].Kind == MediaKind.Video && inputs[index].TargetExtension == ".mov").ToList();
        var paired = new HashSet<int>();

        foreach (var video in videos)
        {
            var identifier = inputs[video].ContentIdentifier;
            var photo = identifier is null
                ? -1
                : photos.FirstOrDefault(p => !paired.Contains(p) && inputs[p].ContentIdentifier == identifier, -1);
            if (photo >= 0)
            {
                pairs[video] = photo;
                paired.Add(photo);
            }
        }

        foreach (var video in videos.Where(v => !pairs.ContainsKey(v)))
        {
            var stem = Path.GetFileNameWithoutExtension(inputs[video].FullPath);
            var photo = photos.FirstOrDefault(
                p => !paired.Contains(p) && string.Equals(Path.GetFileNameWithoutExtension(inputs[p].FullPath), stem, StringComparison.OrdinalIgnoreCase),
                -1);
            if (photo >= 0)
            {
                pairs[video] = photo;
                paired.Add(photo);
            }
        }

        return pairs;
    }

    private static string Prefix(NamingInput input) => input.Kind == MediaKind.Video ? "VID" : "IMG";
}
