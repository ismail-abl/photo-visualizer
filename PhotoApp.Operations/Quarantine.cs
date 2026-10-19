using System.Globalization;
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <summary>
/// Quarantaine (cadre §7) : déplace un fichier sous un dossier donné, enregistre son emplacement d'origine, le restaure ou le purge.
/// Le dossier de quarantaine doit exister. Le fichier est déplacé avant d'être enregistré : un plantage entre les deux laisse un
/// fichier sous la racine, jamais un enregistrement sans fichier.
/// </summary>
public sealed class Quarantine(string rootFolder, IQuarantineLog log, Func<DateTime>? utcNow = null)
{
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public QuarantineEntry Hold(string path, long? batchId = null)
    {
        if (!Directory.Exists(rootFolder))
        {
            throw new DirectoryNotFoundException($"Le dossier de quarantaine n'existe pas : {rootFolder}");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fichier introuvable.", path);
        }

        var now = _utcNow();
        var stored = Path.Combine(rootFolder, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
        File.Move(path, stored, overwrite: false);
        return log.Get(log.Add(now, path, stored, batchId))!;
    }

    public void Restore(long id)
    {
        var entry = RequireHeld(id);
        if (File.Exists(entry.OriginalPath))
        {
            throw new IOException($"Le nom d'origine est occupé : {entry.OriginalPath}");
        }

        var folder = Path.GetDirectoryName(entry.OriginalPath)!;
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Le dossier d'origine n'existe plus : {folder}");
        }

        File.Move(entry.StoredPath, entry.OriginalPath, overwrite: false);
        RemoveEmptyFolder(entry.StoredPath);
        log.SetState(id, QuarantineState.Restored);
    }

    public void Purge(long id)
    {
        var entry = RequireHeld(id);
        File.Delete(entry.StoredPath);
        RemoveEmptyFolder(entry.StoredPath);
        log.SetState(id, QuarantineState.Purged);
    }

    /// <summary>Purge les entrées encore en quarantaine depuis plus que <paramref name="age"/> ; renvoie leur nombre.</summary>
    public int PurgeOlderThan(TimeSpan age)
    {
        var limit = _utcNow() - age;
        var purged = 0;
        foreach (var entry in log.List(QuarantineState.Held).Where(entry => entry.CreatedUtc < limit))
        {
            Purge(entry.Id);
            purged++;
        }

        return purged;
    }

    private QuarantineEntry RequireHeld(long id)
    {
        var entry = log.Get(id) ?? throw new InvalidOperationException($"Entrée de quarantaine {id} introuvable.");
        return entry.State == QuarantineState.Held
            ? entry
            : throw new InvalidOperationException($"L'entrée {id} n'est plus en quarantaine ({entry.State}).");
    }

    private static void RemoveEmptyFolder(string storedPath)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(storedPath)!);   // sans récursivité : échoue s'il reste quelque chose
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au mieux.
        }
    }
}
