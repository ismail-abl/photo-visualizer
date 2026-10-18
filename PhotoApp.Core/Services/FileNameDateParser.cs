using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public class FileNameDateParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Exports WeChat / Facebook : le nombre est un instant UTC (secondes ou millisecondes).
    private static readonly Regex EpochPattern = new(
        "(?:mmexport|FB_IMG[_-])(?<epoch>\\d{13}|\\d{10})(?!\\d)",
        Options | RegexOptions.IgnoreCase);

    // Motifs avec année, mois, jour et heure explicites, du plus précis au moins précis.
    private static readonly Regex[] TimePatterns =
    [
        // 20230304_132015 / 20230304-132015 / Pixel 20230304_132015123
        new("(?<!\\d)(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})[_-](?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})(?:\\d{3})?(?!\\d)", Options),
        // 2025-08-27 21-16-50 / 2025.08.27_21.16.50
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", Options),
        // 2024-01-05 at 3.04.12 PM / 2024-01-05 à 15.04.12
        new("(?<!\\d)(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2}) (?:at|à) (?<hour>\\d{1,2})[.:](?<minute>\\d{2})[.:](?<second>\\d{2})(?:\\s?(?<ampm>[AaPp][Mm]))?(?!\\d)", Options),
        // Capture Windows : Screenshot 2025-08-27 211650
        new("(?<!\\d)(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})[ _](?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})(?!\\d)", Options),
        // Nextcloud iOS : 24-03-02 13-53-44
        new("(?<!\\d)(?<year>\\d{2})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", Options),
        // Date ISO suivie d'une heure facultative
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})(?:[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2}))?(?!\\d)", Options)
    ];

    // 15.01.2025 00_54_14 (JJ-MM-AAAA) ou 12.25.2024 10-30-00 (MM-JJ-AAAA)
    private static readonly Regex DayMonthYearPattern = new(
        "(?<!\\d)(?<first>\\d{2})[.-](?<second>\\d{2})[.-](?<year>\\d{4})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<secondPart>\\d{2})(?!\\d)",
        Options);

    private static readonly Regex DateOnlyPattern = new("(?<!\\d)(?<date>\\d{8})(?!\\d)", Options);

    public virtual ImageDateCandidate? Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var now = DateTime.UtcNow;

        var epoch = TryParseEpoch(fileName);
        if (epoch is not null)
        {
            return Plausible(epoch, now);
        }

        foreach (var pattern in TimePatterns)
        {
            var match = pattern.Match(fileName);
            if (match.Success && TryBuildFromGroups(match, out var wallClock, out var hasTime))
            {
                return Plausible(new ImageDateCandidate(wallClock, hasTime), now);
            }
        }

        var dayMonth = DayMonthYearPattern.Match(fileName);
        if (dayMonth.Success)
        {
            return TryParseDayMonthYear(dayMonth, now);
        }

        var dateOnlyMatch = DateOnlyPattern.Match(fileName);
        if (dateOnlyMatch.Success &&
            DateTime.TryParseExact(dateOnlyMatch.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return Plausible(new ImageDateCandidate(dateOnly, false), now);
        }

        return null;
    }

    private static ImageDateCandidate? Plausible(ImageDateCandidate candidate, DateTime now) =>
        DatePlausibility.IsPlausible(candidate, now) ? candidate : null;

    private static ImageDateCandidate? TryParseEpoch(string fileName)
    {
        var match = EpochPattern.Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        var text = match.Groups["epoch"].Value;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        try
        {
            var instant = text.Length == 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
            return new ImageDateCandidate(instant.UtcDateTime, true, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ImageDateCandidate? TryParseDayMonthYear(Match match, DateTime now)
    {
        var first = Number(match, "first");
        var second = Number(match, "second");

        int day;
        int month;
        if (first > 12)
        {
            (day, month) = (first, second);
        }
        else if (second > 12)
        {
            (day, month) = (second, first);
        }
        else if (first == second)
        {
            (day, month) = (first, second);
        }
        else
        {
            // JJ-MM et MM-JJ sont tous deux valides : on ne devine pas (résolu en phase 1 par profil/voisinage).
            return null;
        }

        return TryBuild(Number(match, "year"), month, day, Number(match, "hour"), Number(match, "minute"), Number(match, "secondPart"), out var wallClock)
            ? Plausible(new ImageDateCandidate(wallClock, true), now)
            : null;
    }

    private static bool TryBuildFromGroups(Match match, out DateTime wallClock, out bool hasTime)
    {
        hasTime = match.Groups["hour"].Success;
        var year = Number(match, "year");
        if (match.Groups["year"].Value.Length == 2)
        {
            year += 2000;
        }

        var hour = hasTime ? Number(match, "hour") : 0;
        if (match.Groups["ampm"].Success)
        {
            if (hour is < 1 or > 12)
            {
                wallClock = default;
                return false;
            }

            var isPm = char.ToUpperInvariant(match.Groups["ampm"].Value[0]) == 'P';
            hour = (hour % 12) + (isPm ? 12 : 0);
        }

        return TryBuild(
            year,
            Number(match, "month"),
            Number(match, "day"),
            hour,
            hasTime ? Number(match, "minute") : 0,
            hasTime ? Number(match, "second") : 0,
            out wallClock);
    }

    private static bool TryBuild(int year, int month, int day, int hour, int minute, int second, out DateTime wallClock)
    {
        try
        {
            wallClock = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            wallClock = default;
            return false;
        }
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
