using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

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
            // Convert using ImageSharp
            using var image = Image.Load(inputPath);
            var encoder = new JpegEncoder { Quality = quality };
            image.Save(outputPath, encoder);
        }
        else
        {
            throw new NotSupportedException($"Conversion from {extension} to JPEG is not supported.");
        }
    }
}