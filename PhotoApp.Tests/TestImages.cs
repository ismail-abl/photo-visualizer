using ImageMagick;

namespace PhotoApp.Tests;

/// <summary>Génère de petites images d'exemple : le corpus de test ne contient aucune donnée privée.</summary>
public static class TestImages
{
    public static void Write(string path, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        image.Write(path, format);
    }

    public static void WritePng(string path) => Write(path, MagickFormat.Png);

    public static void WriteJpegWithExif(string path, Action<ExifProfile> configure)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        var exif = new ExifProfile();
        configure(exif);
        image.SetProfile(exif);
        image.Write(path, MagickFormat.Jpeg);
    }

    public static void WriteJpeg(string path, string? exifDateTimeOriginal = null, string? exifOffsetTimeOriginal = null)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        if (exifDateTimeOriginal is not null || exifOffsetTimeOriginal is not null)
        {
            var exif = new ExifProfile();
            if (exifDateTimeOriginal is not null)
            {
                exif.SetValue(ExifTag.DateTimeOriginal, exifDateTimeOriginal);
            }

            if (exifOffsetTimeOriginal is not null)
            {
                exif.SetValue(ExifTag.OffsetTimeOriginal, exifOffsetTimeOriginal);
            }

            image.SetProfile(exif);
        }

        image.Write(path, MagickFormat.Jpeg);
    }
}
