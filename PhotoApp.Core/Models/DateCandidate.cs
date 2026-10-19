namespace PhotoApp.Core.Models;

/// <summary>
/// Date lue dans une source. Hors UTC, elle garde l'heure murale telle qu'écrite (heure locale du lieu de prise de vue),
/// avec un décalage seulement quand la source le fournit. En UTC (vidéo, GPS, epoch), l'heure murale du lieu est inconnue.
/// </summary>
public sealed record DateCandidate
{
    public DateCandidate(
        DateTime wallClock,
        DatePrecision precision,
        CandidateOrigin origin = CandidateOrigin.Unknown,
        TimeSpan? offset = null,
        bool isUtc = false)
    {
        WallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        Precision = precision;
        Origin = origin;
        IsUtc = isUtc;
        Offset = isUtc ? null : offset;
    }

    public DateTime WallClock { get; init; }

    public DatePrecision Precision { get; init; }

    public CandidateOrigin Origin { get; init; }

    /// <summary>Décalage UTC de l'heure murale, quand la source le donne ou qu'il a été déduit. Toujours null en UTC.</summary>
    public TimeSpan? Offset { get; init; }

    public bool IsUtc { get; init; }

    public bool HasTime => Precision != DatePrecision.Day;

    /// <summary>Instant réel connu sans hypothèse de fuseau : UTC, ou heure locale avec décalage.</summary>
    public bool HasKnownInstant => IsUtc || Offset is not null;

    /// <summary>Instant réel : UTC tel quel, sinon le décalage connu, sinon celui du fuseau de repli à cette date.</summary>
    public DateTimeOffset ToInstant(TimeZoneInfo fallbackZone)
    {
        if (IsUtc)
        {
            return new DateTimeOffset(WallClock, TimeSpan.Zero);
        }

        var offset = Offset ?? fallbackZone.GetUtcOffset(WallClock);
        return new DateTimeOffset(WallClock, offset);
    }

    /// <summary>Heure murale dans un fuseau donné, pour une source qui ne connaît que l'UTC. Une heure locale est rendue telle quelle.</summary>
    public DateCandidate Localize(TimeZoneInfo zone)
    {
        if (!IsUtc)
        {
            return this;
        }

        var utc = DateTime.SpecifyKind(WallClock, DateTimeKind.Utc);
        var offset = zone.GetUtcOffset(utc);
        return this with
        {
            WallClock = DateTime.SpecifyKind(utc + offset, DateTimeKind.Unspecified),
            Offset = offset,
            IsUtc = false
        };
    }
}
