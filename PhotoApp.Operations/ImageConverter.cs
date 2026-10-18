using ImageMagick;

namespace PhotoApp.Operations;

public sealed class ImageConverter : IImageConverter
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    // GIF est exclu : la conversion ne garderait que la première image d'une animation.
    private static readonly HashSet<string> DecodableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif"
    };

    public static bool CanConvert(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        (JpegExtensions.Contains(extension) || DecodableExtensions.Contains(extension));

    public void ConvertToJpeg(string inputPath, string outputPath, int quality)
    {
        var extension = Path.GetExtension(inputPath);
        if (JpegExtensions.Contains(extension))
        {
            // Déjà du JPEG : copie telle quelle pour conserver tous les octets et métadonnées.
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        if (!DecodableExtensions.Contains(extension))
        {
            throw new NotSupportedException($"Conversion from {extension} to JPEG is not supported.");
        }

        // Magick.NET conserve les profils EXIF, XMP et ICC à l'écriture.
        using var image = new MagickImage(inputPath);
        image.AutoOrient();
        if (image.HasAlpha)
        {
            // Le JPEG n'a pas de transparence : fond blanc plutôt que noir.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.Quality = (uint)quality;
        image.Write(outputPath, MagickFormat.Jpeg);
    }
}
