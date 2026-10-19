namespace PhotoApp.Core.Normalization;

/// <summary>Identifiants des règles de décision (spec de phase 1, §5.4), repris tels quels dans le rapport CSV.</summary>
public static class Reasons
{
    public const string MessagingExif = "messagerie-exif";
    public const string MessagingName = "messagerie-nom";
    public const string ScreenshotName = "capture-nom";
    public const string ExifWithOffset = "exif-decalage";
    public const string QuickTimeApple = "quicktime-apple";
    public const string ExifConcordant = "exif-concordant";
    public const string ExifOnly = "exif-seul";
    public const string VideoUtcName = "video-utc-nom";
    public const string ProfileName = "nom-profil";
    public const string XmpOnly = "xmp-seul";
    public const string UtcOnly = "utc-seul";
    public const string GenericName = "nom-generique";
    public const string GenericNameDay = "nom-generique-jour";
    public const string Ambiguous = "ambigue";
    public const string FileDateOnly = "date-fichier-seule";
    public const string Nothing = "aucune";
    public const string Disagreement = "desaccord";
    public const string ReadFailed = "lecture-impossible";
}
