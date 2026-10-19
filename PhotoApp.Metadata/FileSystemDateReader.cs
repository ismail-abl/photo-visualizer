using PhotoApp.Core.Models;

namespace PhotoApp.Metadata;

public class FileSystemDateReader
{
    public virtual DateCandidate? Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var wallClock = fileInfo.LastWriteTime;
            return new DateCandidate(wallClock, DatePrecision.Second, CandidateOrigin.FileModified, TimeZoneInfo.Local.GetUtcOffset(wallClock));
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
