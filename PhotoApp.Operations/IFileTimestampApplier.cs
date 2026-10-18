namespace PhotoApp.Operations;

public interface IFileTimestampApplier
{
    void ApplyTimestamps(string filePath, DateTimeOffset date);
}