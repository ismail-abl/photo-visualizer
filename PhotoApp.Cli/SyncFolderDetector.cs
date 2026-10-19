namespace PhotoApp.Cli;

/// <summary>Reconnaît un dossier synchronisé par Nextcloud (cadre §7) : un fichier de base de synchronisation dans le dossier ou l'un de ses parents.</summary>
public static class SyncFolderDetector
{
    private static readonly string[] Markers = [".nextcloudsync.log", ".csync_journal.db", ".sync_journal.db"];

    public static bool IsSynced(string folder)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(folder)); current is not null; current = current.Parent)
        {
            try
            {
                if (Markers.Any(marker => File.Exists(Path.Combine(current.FullName, marker)))
                    || current.EnumerateFiles(".sync_*.db").Any())
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier parent illisible : on continue vers le haut.
            }
        }

        return false;
    }
}
