namespace PhotoApp.Operations;

public static class FileMoves
{
    /// <summary>
    /// Renomme un fichier sans jamais écraser : lève <see cref="IOException"/> si la cible existe. Un changement de casse seul
    /// (<c>IMG-1.JPG</c> → <c>IMG-1.jpg</c>) passe par un nom voisin, pour ne dépendre d'aucun comportement propre au système de fichiers.
    /// </summary>
    public static void Move(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            var temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Move(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: false);
            return;
        }

        File.Move(source, target, overwrite: false);
    }
}
