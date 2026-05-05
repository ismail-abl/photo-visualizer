namespace PhotoApp.Core.Services;

public static class SupportedImageExtensions
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".heic",
        ".heif",
        ".webp",
        ".tiff",
        ".tif"
    };

    public static bool IsSupported(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return Extensions.Contains(extension);
    }
}
