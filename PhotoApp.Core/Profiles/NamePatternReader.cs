using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Profiles;

/// <summary>Construit la lecture d'un nom à partir des groupes nommés d'un motif. Aucune plausibilité ici : l'arbitre s'en charge.</summary>
public static class NamePatternReader
{
    public static NameReading Read(Match match, SourceProfile? profile, string patternKey, bool isDerivative)
    {
        var semantics = profile?.TimeSemantics ?? NameTimeSemantics.Local;
        var reading = new NameReading(profile, null, null, Counter(match), profile is not null, isDerivative);

        if (match.Groups["epoch"].Success)
        {
            return reading with { Date = Epoch(match.Groups["epoch"].Value) };
        }

        if (match.Groups["d1"].Success)
        {
            return ReadDayMonth(match, reading, patternKey, profile?.DayMonthOrder ?? DayMonthOrder.Unknown, semantics);
        }

        if (!match.Groups["year"].Success)
        {
            return reading;
        }

        return reading with { Date = Build(match, Number(match, "month"), Number(match, "day"), semantics) };
    }

    private static NameReading ReadDayMonth(Match match, NameReading reading, string patternKey, DayMonthOrder declared, NameTimeSemantics semantics)
    {
        var first = Number(match, "d1");
        var second = Number(match, "d2");
        var dayFirst = Build(match, month: second, day: first, semantics);
        var monthFirst = Build(match, month: first, day: second, semantics);

        if (first == second)
        {
            return reading with { Date = dayFirst };
        }

        // Une seule lecture valide (composante > 12) : elle prouve l'ordre du motif pour les voisins.
        if (monthFirst is null)
        {
            return dayFirst is null ? reading : reading with { Date = dayFirst, Evidence = new DayMonthEvidence(patternKey, DayMonthOrder.DayFirst) };
        }

        if (dayFirst is null)
        {
            return reading with { Date = monthFirst, Evidence = new DayMonthEvidence(patternKey, DayMonthOrder.MonthFirst) };
        }

        return declared switch
        {
            DayMonthOrder.DayFirst => reading with { Date = dayFirst },
            DayMonthOrder.MonthFirst => reading with { Date = monthFirst },
            _ => reading with { Ambiguous = new AmbiguousName(dayFirst, monthFirst, patternKey) }
        };
    }

    private static DateCandidate? Build(Match match, int month, int day, NameTimeSemantics semantics)
    {
        var year = Number(match, "year");
        if (match.Groups["year"].Length == 2)
        {
            year += 2000;
        }

        var precision = DatePrecision.Day;
        int hour = 0, minute = 0, second = 0, millisecond = 0;
        if (match.Groups["hour"].Success)
        {
            hour = Number(match, "hour");
            minute = Number(match, "minute");
            precision = DatePrecision.Minute;

            if (match.Groups["ampm"].Success)
            {
                if (hour is < 1 or > 12)
                {
                    return null;
                }

                var isPm = char.ToUpperInvariant(match.Groups["ampm"].Value[0]) == 'P';
                hour = hour % 12 + (isPm ? 12 : 0);
            }

            if (match.Groups["second"].Success)
            {
                second = Number(match, "second");
                precision = DatePrecision.Second;
            }

            if (match.Groups["fraction"].Success && SubSeconds.ToMilliseconds(match.Groups["fraction"].Value) is { } ms)
            {
                millisecond = ms;
                precision = DatePrecision.SubSecond;
            }
        }

        try
        {
            var value = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
            return new DateCandidate(value, precision, CandidateOrigin.FileName, isUtc: semantics == NameTimeSemantics.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Exports WeChat, Facebook… : le nombre est un instant UTC, en secondes (10 chiffres) ou millisecondes (13).
    private static DateCandidate? Epoch(string text)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        try
        {
            var milliseconds = text.Length == 13;
            var instant = milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value);
            return new DateCandidate(
                instant.UtcDateTime, milliseconds ? DatePrecision.SubSecond : DatePrecision.Second, CandidateOrigin.FileName, isUtc: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long? Counter(Match match) =>
        match.Groups["counter"].Success && long.TryParse(match.Groups["counter"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var counter)
            ? counter
            : null;

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
