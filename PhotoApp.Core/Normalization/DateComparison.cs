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
    /// L'EXIF d'un téléphone est écrit jusqu'à quelques secondes après l'heure de son nom
    /// (mesure réelle : Samsung +2 à +4 s, Xiaomi jusqu'à +10 s).
    /// </summary>
    public static readonly TimeSpan ExifLatency = TimeSpan.FromSeconds(10);

    /// <summary>
    /// L'en-tête <c>mvhd</c> d'une vidéo Android ou iPhone est souvent écrit à la fin de l'enregistrement (écart = durée de la vidéo,
    /// mesure réelle). 7 min reste sous le demi-quart d'heure, donc l'arrondi du fuseau reste juste.
    /// </summary>
    public static readonly TimeSpan MovieHeaderLag = TimeSpan.FromMinutes(7);

    /// <summary>
    /// Deux valeurs concordent à la précision la plus faible des deux. Deux instants connus se comparent comme instants ;
    /// deux heures locales, comme heures murales. Une heure locale flottante face à un UTC relève de <see cref="ZoneGap"/>.
    /// </summary>
    public static bool Concordant(DateCandidate a, DateCandidate b)
    {
        var weakest = (DatePrecision)Math.Min((int)a.Precision, (int)b.Precision);

        // Jour seul face à une valeur UTC : l'instant doit tomber dans le jour local, quel que soit le fuseau (−12 h à +14 h).
        if (weakest == DatePrecision.Day && a.IsUtc != b.IsUtc)
        {
            var (day, utc) = a.IsUtc ? (b, a) : (a, b);
            if (day.Offset is null)
            {
                var start = day.WallClock.Date - MaxZoneOffset;
                var end = day.WallClock.Date + TimeSpan.FromHours(36);
                return utc.WallClock >= start && utc.WallClock < end;
            }
        }

        DateTime left;
        DateTime right;
        if (a.HasKnownInstant && b.HasKnownInstant)
        {
            // L'en-tête vidéo suit l'heure de début de 0 à 7 min (−2 s toléré).
            if (weakest >= DatePrecision.Second && (a.Origin == CandidateOrigin.QuickTimeMovieHeader) != (b.Origin == CandidateOrigin.QuickTimeMovieHeader))
            {
                var (header, other) = a.Origin == CandidateOrigin.QuickTimeMovieHeader ? (a, b) : (b, a);
                var lag = UtcOf(header) - UtcOf(other);
                return lag >= -SecondTolerance && lag <= MovieHeaderLag;
            }

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

        var precision = weakest;
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

    /// <summary>
    /// L'EXIF concorde avec le nom d'appareil : à la seconde ou mieux, EXIF − nom dans [−2 s, +10 s] (latence d'écriture) ;
    /// à la minute ou au jour, concordance ordinaire.
    /// </summary>
    public static bool ExifMatchesName(DateCandidate exif, DateCandidate name)
    {
        if (exif.IsUtc || name.IsUtc)
        {
            return false;
        }

        if (Math.Min((int)exif.Precision, (int)name.Precision) < (int)DatePrecision.Second)
        {
            return Concordant(exif, name);
        }

        var gap = exif.WallClock - name.WallClock;
        return gap >= -SecondTolerance && gap <= ExifLatency;
    }

    /// <summary>Écart de fuseau valide entre une heure locale sans décalage et une valeur UTC : multiple de 15 min à 2 s près, au plus 14 h.</summary>
    public static TimeSpan? ZoneGap(DateCandidate local, DateCandidate utc)
    {
        if (local.IsUtc || local.Offset is not null || !utc.IsUtc || local.Precision < DatePrecision.Second || utc.Precision < DatePrecision.Second)
        {
            return null;
        }

        // L'en-tête vidéo, écrit à la fin de l'enregistrement, est plus tard que l'heure locale de début.
        var below = utc.Origin == CandidateOrigin.QuickTimeMovieHeader ? MovieHeaderLag : SecondTolerance;
        return RoundToZone(local.WallClock - utc.WallClock, below, SecondTolerance);
    }

    /// <summary>Décalage déduit du GPS : DateTimeOriginal − GPS, arrondi au quart d'heure, reste d'au plus 2 min.</summary>
    public static TimeSpan? OffsetFromGps(DateCandidate exif, DateCandidate gps)
    {
        if (exif.IsUtc || !gps.IsUtc)
        {
            return null;
        }

        return RoundToZone(exif.WallClock - gps.WallClock, GpsTolerance, GpsTolerance);
    }

    /// <summary>Compare les heures murales tronquées à la précision donnée : négatif si a est antérieur.</summary>
    public static int CompareAt(DateCandidate a, DateCandidate b, DatePrecision precision) =>
        Truncate(a.WallClock, precision).CompareTo(Truncate(b.WallClock, precision));

    /// <summary>Arrondit au quart d'heure ; le reste (différence − arrondi) doit être dans [−below, +above].</summary>
    private static TimeSpan? RoundToZone(TimeSpan difference, TimeSpan below, TimeSpan above)
    {
        var steps = Math.Round(difference.Ticks / (double)ZoneStep.Ticks);
        var rounded = TimeSpan.FromTicks((long)steps * ZoneStep.Ticks);
        var remainder = difference - rounded;
        if (remainder < -below || remainder > above || rounded.Duration() > MaxZoneOffset)
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
