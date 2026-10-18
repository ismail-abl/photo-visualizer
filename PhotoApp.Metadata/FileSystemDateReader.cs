using PhotoApp.Core.Models;

namespace PhotoApp.Metadata;

public class FileSystemDateReader
{
    public virtual ImageDateCandidate? Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            return new ImageDateCandidate(new DateTimeOffset(DateTime.SpecifyKind(fileInfo.LastWriteTime, DateTimeKind.Utc)), true);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
