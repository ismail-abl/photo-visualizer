namespace PhotoApp.Core.Batches;

/// <summary>Nom d'un sidecar (.xmp, .aae) d'après le nouveau nom de son média, en gardant la convention du sidecar d'origine.</summary>
public static class SidecarNames
{
    /// <summary>
    /// <c>photo.jpg.xmp</c> suit le nom complet du média ; <c>photo.xmp</c> et <c>IMG_0477.AAE</c> suivent son nom de base.
    /// Null si le sidecar n'appartient pas à ce média.
    /// </summary>
    public static string? Rename(string oldMediaName, string newMediaName, string sidecarName)
    {
        if (sidecarName.StartsWith(oldMediaName + ".", StringComparison.OrdinalIgnoreCase))
        {
            return newMediaName + sidecarName[oldMediaName.Length..];
        }

        var oldStem = Path.GetFileNameWithoutExtension(oldMediaName);
        if (string.Equals(Path.GetFileNameWithoutExtension(sidecarName), oldStem, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(newMediaName) + Path.GetExtension(sidecarName);
        }

        return null;
    }
}
