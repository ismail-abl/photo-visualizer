using System.Globalization;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class NormalizedFileNameBuilder
{
    /// <summary>Nom sans suffixe ni extension. Jour seul : l'heure 000000 sert uniquement au tri (spec §5.5).</summary>
    public static string BaseName(ImageDateCandidate date) => date.HasTime
        ? "IMG-" + date.WallClock.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        : "IMG-" + date.WallClock.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-000000";

    public static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var value = (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
        return value == ".jpeg" ? ".jpg" : value;
    }

    /// <summary>Premier nom possible pour cette date (l'attribution réelle dépend des collisions).</summary>
    public static string Preview(ImageDateCandidate date) =>
        date.HasTime ? BaseName(date) + ".jpg" : BaseName(date) + "-01.jpg";
}
