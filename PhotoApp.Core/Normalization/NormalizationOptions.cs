namespace PhotoApp.Core.Normalization;

/// <param name="UtcNow">Borne haute de plausibilité (aujourd'hui + 1 jour).</param>
/// <param name="LocalZone">Fuseau supposé quand une source ne connaît que l'UTC (vidéo, epoch, date du fichier).</param>
public sealed record NormalizationOptions(DateTime UtcNow, TimeZoneInfo LocalZone)
{
    public static NormalizationOptions ForNow() => new(DateTime.UtcNow, TimeZoneInfo.Local);
}
