using System.Globalization;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Textes affichés par l'interface et le rapport : libellés français, dates au format invariant.</summary>
public static class DecisionTexts
{
    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.Ordinal)
    {
        [Reasons.AlreadyNormalized] = "Nom déjà normalisé par PhotoManager (fait foi)",
        [Reasons.MessagingExif] = "EXIF de la photo d'origine (fichier reçu par messagerie)",
        [Reasons.MessagingName] = "Nom donné par la messagerie (fait foi)",
        [Reasons.ScreenshotName] = "Nom de la capture d'écran",
        [Reasons.ExifWithOffset] = "EXIF avec fuseau",
        [Reasons.QuickTimeApple] = "Date Apple de la vidéo",
        [Reasons.ExifConcordant] = "EXIF confirmé par le nom ou le GPS",
        [Reasons.ExifBeforeName] = "EXIF d'origine (fichier édité ou enregistré après la prise de vue)",
        [Reasons.ExifOnly] = "EXIF sans fuseau",
        [Reasons.VideoUtcName] = "Date de la vidéo confirmée par le nom",
        [Reasons.ProfileName] = "Nom donné par l'appareil",
        [Reasons.XmpOnly] = "Date XMP",
        [Reasons.UtcOnly] = "Date UTC de la vidéo, fuseau du PC supposé",
        [Reasons.GenericName] = "Date trouvée dans le nom",
        [Reasons.GenericNameDay] = "Jour trouvé dans le nom, sans heure",
        [Reasons.Ambiguous] = "Date ambiguë (jour et mois inversables)",
        [Reasons.FileDateOnly] = "Seulement la date du fichier (souvent celle de la copie)",
        [Reasons.Nothing] = "Aucune date trouvée",
        [Reasons.Disagreement] = "Sources en désaccord",
        [Reasons.ReadFailed] = "Fichier illisible"
    };

    public static string ConfidenceLabel(Confidence confidence) => confidence switch
    {
        Confidence.Certain => "Sûre",
        Confidence.Probable => "Probable",
        Confidence.Uncertain => "Incertaine",
        _ => "Aucune"
    };

    public static string ReasonLabel(string reason) => ReasonLabels.GetValueOrDefault(reason, reason);

    public static string PrecisionLabel(DatePrecision precision) => precision switch
    {
        DatePrecision.Day => "jour",
        DatePrecision.Minute => "minute",
        DatePrecision.Second => "seconde",
        _ => "sous-seconde"
    };

    public static IReadOnlyList<string> FlagLabels(DecisionFlags flags)
    {
        var labels = new List<string>();
        Add(DecisionFlags.Ambiguous, "ambigue");
        Add(DecisionFlags.Disagreement, "desaccord");
        Add(DecisionFlags.RealFormat, "format-reel");
        Add(DecisionFlags.AssumedZone, "fuseau-suppose");
        Add(DecisionFlags.Derivative, "derive");
        Add(DecisionFlags.LivePhoto, "live-photo");
        return labels;

        void Add(DecisionFlags flag, string label)
        {
            if (flags.HasFlag(flag))
            {
                labels.Add(label);
            }
        }
    }

    public static string FormatDate(DateCandidate date) => date.WallClock.ToString(
        date.Precision switch
        {
            DatePrecision.Day => "yyyy-MM-dd",
            DatePrecision.Minute => "yyyy-MM-dd HH:mm",
            DatePrecision.Second => "yyyy-MM-dd HH:mm:ss",
            _ => "yyyy-MM-dd HH:mm:ss.fff"
        },
        CultureInfo.InvariantCulture);

    public static string FormatOffset(TimeSpan? offset) => offset is { } value
        ? (value < TimeSpan.Zero ? "-" : "+") + value.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)
        : string.Empty;

    public static string FormatCandidate(DateCandidate candidate) =>
        FormatDate(candidate) + (candidate.IsUtc ? " UTC" : FormatOffset(candidate.Offset));
}
