namespace PhotoApp.Operations;

public static class FileMoves
{
    /// <summary>
    /// Renomme un fichier sans jamais écraser : lève <see cref="IOException"/> si la cible existe. Un changement de casse seul
    /// (<c>IMG-1.JPG</c> → <c>IMG-1.jpg</c>) passe par un nom voisin, pour ne dépendre d'aucun comportement propre au système de fichiers.
    /// </summary>
    public static void Move(string source, string target) => Move(source, target, (s, t) => File.Move(s, t, overwrite: false));

    // Variante interne : le déplacement est injectable, pour provoquer un échec de la seconde étape (impossible à obtenir sur NTFS, où la cible d'un changement de casse seul est libre).
    internal static void Move(string source, string target, Action<string, string> moveFile)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            var temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            // Un échec du premier déplacement ne laisse rien à restaurer : l'exception remonte telle quelle.
            moveFile(source, temporary);
            try
            {
                moveFile(temporary, target);
            }
            catch (Exception secondStepFailure)
            {
                // Échec de la seconde étape (partage transitoire, cible apparue…) : on remet le fichier sous son nom d'origine, puis on relance l'exception d'origine.
                try
                {
                    moveFile(temporary, source);
                }
                catch (Exception rollbackFailure)
                {
                    throw new IOException(
                        $"Changement de casse interrompu : le fichier est resté sous le nom temporaire « {temporary} » " +
                        $"(retour arrière impossible : {rollbackFailure.Message}). Cause d'origine : {secondStepFailure.Message}",
                        secondStepFailure);
                }

                throw;
            }

            return;
        }

        moveFile(source, target);
    }
}
