using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Attribue des noms uniques. Les noms déjà pris ne sont jamais renumérotés :
/// un nouveau venu reçoit le prochain numéro libre.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    public NameAllocator(IEnumerable<string>? alreadyTaken = null)
    {
        if (alreadyTaken is null)
        {
            return;
        }

        foreach (var name in alreadyTaken)
        {
            _taken.Add(name);
        }
    }

    public string Allocate(ImageDateCandidate date, string extension)
    {
        var baseName = NormalizedFileNameBuilder.BaseName(date);
        var normalizedExtension = NormalizedFileNameBuilder.NormalizeExtension(extension);

        // Jour seul : le suffixe est toujours présent ; sinon le premier du lot n'en a pas.
        for (var number = date.HasTime ? 0 : 1; ; number++)
        {
            var name = number == 0
                ? baseName + normalizedExtension
                : $"{baseName}-{number:00}{normalizedExtension}";

            if (_taken.Add(name))
            {
                return name;
            }
        }
    }
}
