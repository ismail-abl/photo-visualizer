namespace PhotoApp.Core.Models;

public sealed class ImageDateCandidate
{
    public ImageDateCandidate(DateTimeOffset date, bool hasTime)
    {
        Date = date;
        HasTime = hasTime;
    }

    public DateTimeOffset Date { get; }

    public bool HasTime { get; }
}
