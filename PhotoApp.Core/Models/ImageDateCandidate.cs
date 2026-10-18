namespace PhotoApp.Core.Models;

/// <summary>
/// Date lue dans une source. Elle garde l'heure murale telle qu'écrite (heure locale du lieu de prise de vue),
/// avec un décalage UTC seulement quand la source le fournit.
/// </summary>
public sealed class ImageDateCandidate
{
    public ImageDateCandidate(DateTime wallClock, bool hasTime, TimeSpan? offset = null)
    {
        WallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        HasTime = hasTime;
        Offset = offset;
    }

    public DateTime WallClock { get; }

    public bool HasTime { get; }

    public TimeSpan? Offset { get; }

    /// <summary>Instant réel : le décalage connu, sinon celui du fuseau de repli à cette date.</summary>
    public DateTimeOffset ToInstant(TimeZoneInfo fallbackZone)
    {
        var offset = Offset ?? fallbackZone.GetUtcOffset(WallClock);
        return new DateTimeOffset(WallClock, offset);
    }
}
