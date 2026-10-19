namespace PhotoApp.Core.Models;

/// <summary>Un média trouvé par le scan, avec ses sidecars (.xmp, .aae) du même nom de base.</summary>
public sealed record ScannedMedia(string FullPath, long SizeBytes, DateTime LastWriteUtc, IReadOnlyList<string> Sidecars)
{
    public string FileName => Path.GetFileName(FullPath);

    public string Extension => Path.GetExtension(FullPath);
}
