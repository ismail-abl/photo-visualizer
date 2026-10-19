namespace PhotoApp.Operations;

/// <param name="Rank">Rang de l'élément dans le lot : la clé du résultat.</param>
public sealed record PreflightEntry(int Rank, int GroupId, string SourcePath, string TargetPath, long SourceSize, DateTime SourceLastWriteUtc);

/// <summary>Contrôles préalables à l'exécution (spec de phase 2, §5.2). Un fichier qui échoue est écarté avec sa raison ; un groupe est tout ou rien.</summary>
public static class BatchPreflight
{
    // MAX_PATH moins le terminateur ; la limite ne s'applique qu'à Windows.
    private const int WindowsMaxPath = 259;

    /// <summary>Les rangs à écarter, avec leur raison. Les autres membres d'un groupe en échec sont écartés avec « Groupe écarté : … ».</summary>
    public static IReadOnlyDictionary<int, string> Evaluate(IReadOnlyList<PreflightEntry> entries)
    {
        var reasons = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            if (Check(entry) is { } reason)
            {
                reasons[entry.Rank] = reason;
            }
        }

        foreach (var group in entries.GroupBy(entry => entry.GroupId))
        {
            var failing = group.FirstOrDefault(entry => reasons.ContainsKey(entry.Rank));
            if (failing is null)
            {
                continue;
            }

            foreach (var other in group.Where(entry => !reasons.ContainsKey(entry.Rank)))
            {
                reasons[other.Rank] = $"Groupe écarté : {Path.GetFileName(failing.SourcePath)} — {reasons[failing.Rank]}";
            }
        }

        return reasons;
    }

    /// <summary>La raison de l'écart, ou null si le fichier passe tous les contrôles.</summary>
    public static string? Check(PreflightEntry entry)
    {
        var info = new FileInfo(entry.SourcePath);
        if (!info.Exists)
        {
            return "Fichier introuvable";
        }

        if (info.Length != entry.SourceSize || info.LastWriteTimeUtc != entry.SourceLastWriteUtc)
        {
            return "Fichier modifié depuis le plan";
        }

        if (OperatingSystem.IsWindows() && entry.TargetPath.Length > WindowsMaxPath)
        {
            return "Chemin trop long";
        }

        if (TargetTaken(entry))
        {
            return "Le nom cible existe déjà";
        }

        try
        {
            // Une ouverture exclusive échoue si un autre processus tient le fichier.
            using var probe = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException)
        {
            return "Fichier verrouillé";
        }
        catch (UnauthorizedAccessException)
        {
            return "Accès refusé";
        }

        return null;
    }

    private static bool TargetTaken(PreflightEntry entry)
    {
        if (!File.Exists(entry.TargetPath) && !Directory.Exists(entry.TargetPath))
        {
            return false;
        }

        // Changement de casse seul sur un volume insensible à la casse : la cible « existante » est la source elle-même.
        var caseOnly = string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal);
        return !caseOnly;
    }
}
