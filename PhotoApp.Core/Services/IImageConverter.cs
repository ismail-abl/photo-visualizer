namespace PhotoApp.Core.Services;

public interface IImageConverter
{
    void ConvertToJpeg(string inputPath, string outputPath, int quality);
}