using System.IO;
using ImageMagick;

namespace PhotoApp.Core.Services;

public sealed class ImageConverter : IImageConverter
{
    public void ConvertToJpeg(string inputPath, string outputPath, int quality)
    {
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (extension == ".jpg" || extension == ".jpeg")
        {
            // Already JPEG, just copy
            File.Copy(inputPath, outputPath, overwrite: true);
        }
        else if (extension == ".png" || extension == ".bmp" || extension == ".gif" || extension == ".tiff" || extension == ".tif")
        {
            // Convert using Magick.NET
            using var image = new MagickImage(inputPath);
            image.Quality = (uint)quality;
            image.Write(outputPath, MagickFormat.Jpeg);
        }
        else
        {
            throw new NotSupportedException($"Conversion from {extension} to JPEG is not supported.");
        }
    }
}
