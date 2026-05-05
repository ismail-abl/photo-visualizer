namespace PhotoApp.Core.Services;

public interface IFileTimestampApplier
{
    void ApplyTimestamps(string filePath, DateTimeOffset date);
}