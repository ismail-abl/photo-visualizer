using System.Globalization;
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <summary>
/// Quarantaine (cadre §7) : déplace un fichier sous un dossier donné, enregistre son emplacement d'origine, le restaure ou le purge.
/// Le dossier de quarantaine doit exister. Le fichier est déplacé avant d'être enregistré : un plantage entre les deux laisse un
/// fichier sous la racine, jamais un enregistrement sans fichier. Si l'enregistrement échoue par une exception (base verrouillée…),
/// <see cref="Hold(string, long?)"/> remet le fichier à sa place, retire le dossier devenu vide et relance l'exception d'origine ; si le fichier
/// ne peut pas être remis en place, l'<see cref="IOException"/> levée donne son emplacement sous la quarantaine.
/// </summary>
public sealed class Quarantine(string rootFolder, IQuarantineLog log, Func<DateTime>? utcNow = null)
{
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public QuarantineEntry Hold(string path, long? batchId = null) =>
        Hold(path, batchId, (from, to) => File.Move(from, to, overwrite: false));

    /// <summary>Comme <see cref="Hold(string, long?)"/>, avec le déplacement injecté (aussi utilisé pour remettre le fichier en place) : sert aux tests.</summary>
    internal QuarantineEntry Hold(string path, long? batchId, Action<string, string> moveFile)
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
        moveFile(path, stored);

        long id;
        try
        {
            id = log.Add(now, path, stored, batchId);
        }
        catch (Exception ex)
        {
            PutBack(path, stored, moveFile, ex);
            throw;
        }

        return log.Get(id)!;
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

    /// <summary>
    /// Défait le déplacement de <see cref="Hold(string, long?)"/> quand l'enregistrement a échoué. Si le fichier ne peut pas être remis en place,
    /// lève une <see cref="IOException"/> qui donne son emplacement sous la quarantaine (l'exception d'origine en est la cause).
    /// </summary>
    private static void PutBack(string path, string stored, Action<string, string> moveFile, Exception cause)
    {
        try
        {
            moveFile(stored, path);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"L'enregistrement en quarantaine a échoué et le fichier n'a pas pu être remis en place ({ex.Message}). Il se trouve ici : {stored}",
                cause);
        }

        RemoveEmptyFolder(stored);
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
