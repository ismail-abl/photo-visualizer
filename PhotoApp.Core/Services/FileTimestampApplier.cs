using System.IO;

namespace PhotoApp.Core.Services;

public sealed class FileTimestampApplier : IFileTimestampApplier
{
    public void ApplyTimestamps(string filePath, DateTimeOffset date)
    {
        var utcDate = date.UtcDateTime;
        File.SetCreationTimeUtc(filePath, utcDate);
        File.SetLastWriteTimeUtc(filePath, utcDate);
        File.SetLastAccessTimeUtc(filePath, utcDate);
    }
}