namespace PhotoApp.Operations;

public interface IImageConverter
{
    void ConvertToJpeg(string inputPath, string outputPath, int quality);
}