namespace PhotoApp.Operations;

public readonly record struct FileTimes(DateTime CreationUtc, DateTime LastWriteUtc);

/// <summary>Dates de création et de modification d'un fichier, lues et écrites ensemble (le renommage sur place ne touche pas à la date d'accès).</summary>
public static class FileTimestamps
{
    public static FileTimes Read(string path)
    {
        var info = new FileInfo(path);
        return new FileTimes(info.CreationTimeUtc, info.LastWriteTimeUtc);
    }

    public static void Set(string path, DateTime creationUtc, DateTime lastWriteUtc)
    {
        File.SetCreationTimeUtc(path, creationUtc);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
