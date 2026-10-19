namespace PhotoApp.Core.Services;

/// <summary>Extensions prises en charge (avec le point), et équivalences d'un même format.</summary>
public static class SupportedMediaExtensions
{
    private static readonly HashSet<string> Photos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".tif", ".tiff", ".avif"
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mov", ".mp4" };

    private static readonly HashSet<string> Sidecars = new(StringComparer.OrdinalIgnoreCase) { ".xmp", ".aae" };

    // MetadataExtractor annonce l'extension la plus courante d'un format : heic pour .heif, tiff pour .tif, mp4 pour .m4v.
    private static readonly string[][] SameFormatGroups =
    [
        [".jpg", ".jpeg", ".jpe"],
        [".heic", ".heif"],
        [".tif", ".tiff"],
        [".mp4", ".m4v"]
    ];

    public static bool IsPhoto(string? extension) => extension is not null && Photos.Contains(extension);

    public static bool IsVideo(string? extension) => extension is not null && Videos.Contains(extension);

    public static bool IsSidecar(string? extension) => extension is not null && Sidecars.Contains(extension);

    public static bool IsSupported(string? extension) => IsPhoto(extension) || IsVideo(extension);

    public static bool SameFormat(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase)
        || SameFormatGroups.Any(group =>
            group.Contains(first, StringComparer.OrdinalIgnoreCase) && group.Contains(second, StringComparer.OrdinalIgnoreCase));
}
