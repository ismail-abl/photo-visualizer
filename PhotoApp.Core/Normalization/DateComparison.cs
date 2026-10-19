using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Tolérances de la spec de phase 1 (§5.2), revues après la mesure de mi-phase.</summary>
public static class DateComparison
{
    public static readonly TimeSpan SecondTolerance = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ZoneStep = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxZoneOffset = TimeSpan.FromHours(14);
    public static readonly TimeSpan GpsTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Deux valeurs concordent à la précision la plus faible des deux. Deux instants connus se comparent comme instants ;
    /// deux heures locales, comme heures murales. Une heure locale flottante face à un UTC relève de <see cref="ZoneGap"/>.
    /// </summary>
    public static bool Concordant(DateCandidate a, DateCandidate b)
    {
        DateTime left;
        DateTime right;
        if (a.HasKnownInstant && b.HasKnownInstant)
        {
            left = UtcOf(a);
            right = UtcOf(b);
        }
        else if (!a.IsUtc && !b.IsUtc)
        {
            left = a.WallClock;
            right = b.WallClock;
        }
        else
        {
            return false;
        }

        var precision = (DatePrecision)Math.Min((int)a.Precision, (int)b.Precision);
        if (precision == DatePrecision.Day && !a.IsUtc && !b.IsUtc)
        {
            // Le jour se lit sur l'heure murale : c'est celui qu'affiche l'appareil.
            return a.WallClock.Date == b.WallClock.Date;
        }

        return precision switch
        {
            DatePrecision.Day => left.Date == right.Date,
            DatePrecision.Minute => Truncate(left, DatePrecision.Minute) == Truncate(right, DatePrecision.Minute),
            _ => (left - right).Duration() <= SecondTolerance
        };
    }

    /// <summary>Écart de fuseau valide entre une heure locale et une valeur UTC : multiple de 15 min à 2 s près, au plus 14 h.</summary>
    public static TimeSpan? ZoneGap(DateCandidate local, DateCandidate utc)
    {
        if (local.IsUtc || !utc.IsUtc || local.Precision < DatePrecision.Second || utc.Precision < DatePrecision.Second)
        {
            return null;
        }

        return RoundToZone(local.WallClock - utc.WallClock, SecondTolerance);
    }

    /// <summary>Décalage déduit du GPS : DateTimeOriginal − GPS, arrondi au quart d'heure, reste d'au plus 2 min.</summary>
    public static TimeSpan? OffsetFromGps(DateCandidate exif, DateCandidate gps)
    {
        if (exif.IsUtc || !gps.IsUtc)
        {
            return null;
        }

        return RoundToZone(exif.WallClock - gps.WallClock, GpsTolerance);
    }

    /// <summary>Compare les heures murales tronquées à la précision donnée : négatif si a est antérieur.</summary>
    public static int CompareAt(DateCandidate a, DateCandidate b, DatePrecision precision) =>
        Truncate(a.WallClock, precision).CompareTo(Truncate(b.WallClock, precision));

    private static TimeSpan? RoundToZone(TimeSpan difference, TimeSpan tolerance)
    {
        var steps = Math.Round(difference.Ticks / (double)ZoneStep.Ticks);
        var rounded = TimeSpan.FromTicks((long)steps * ZoneStep.Ticks);
        if ((difference - rounded).Duration() > tolerance || rounded.Duration() > MaxZoneOffset)
        {
            return null;
        }

        return rounded;
    }

    private static DateTime UtcOf(DateCandidate candidate) =>
        candidate.IsUtc ? candidate.WallClock : candidate.WallClock - candidate.Offset!.Value;

    private static DateTime Truncate(DateTime value, DatePrecision precision) => precision switch
    {
        DatePrecision.Day => value.Date,
        DatePrecision.Minute => new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute),
        DatePrecision.Second => new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond),
        _ => value
    };
}
