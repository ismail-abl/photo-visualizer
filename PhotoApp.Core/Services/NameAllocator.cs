using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Attribue des noms uniques par nom de base, sans l'extension : deux fichiers de la même seconde ne partagent jamais
/// un nom de base (seule une Live Photo le fait, hors de cet allocateur). Les noms déjà pris ne sont jamais renumérotés :
/// un nouveau venu reçoit le prochain numéro libre.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _takenStems = new(StringComparer.OrdinalIgnoreCase);

    public NameAllocator(IEnumerable<string>? alreadyTaken = null)
    {
        foreach (var name in alreadyTaken ?? [])
        {
            _takenStems.Add(Path.GetFileNameWithoutExtension(name));
        }
    }

    /// <summary>Réserve un nom existant ; faux si son nom de base est déjà pris.</summary>
    public bool TryReserve(string fileName) => _takenStems.Add(Path.GetFileNameWithoutExtension(fileName));

    public string Allocate(DateCandidate date, string extension, string prefix = "IMG")
    {
        var baseName = NormalizedFileNameBuilder.BaseName(date, prefix);
        var normalizedExtension = NormalizedFileNameBuilder.NormalizeExtension(extension);

        // Jour seul : le suffixe est toujours présent ; sinon le premier de la seconde n'en a pas.
        for (var number = date.HasTime ? 0 : 1; ; number++)
        {
            var stem = number == 0 ? baseName : $"{baseName}-{number:00}";
            if (_takenStems.Add(stem))
            {
                return stem + normalizedExtension;
            }
        }
    }
}
