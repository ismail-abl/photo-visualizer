using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Lit une date ISO 8601 (XMP, clés Apple QuickTime) en conservant l'heure écrite : aucune conversion en UTC.
/// « Z » désigne un instant UTC dont l'heure murale du lieu est inconnue.
/// </summary>
public static class XmpDateParser
{
    private static readonly Regex Pattern = new(
        "^(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})(?:[T ](?<hour>\\d{2}):(?<minute>\\d{2})(?::(?<second>\\d{2})(?:[.,](?<fraction>\\d+))?)?)?\\s*(?<zone>Z|[+-]\\d{2}:?\\d{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateCandidate? Parse(string? text, CandidateOrigin origin = CandidateOrigin.Xmp)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hasTime = match.Groups["hour"].Success;
        var hasSecond = match.Groups["second"].Success;
        var millisecond = SubSeconds.ToMilliseconds(match.Groups["fraction"].Value) ?? 0;
        var precision = !hasTime ? DatePrecision.Day
            : match.Groups["fraction"].Success ? DatePrecision.SubSecond
            : hasSecond ? DatePrecision.Second
            : DatePrecision.Minute;

        DateTime wallClock;
        try
        {
            wallClock = new DateTime(
                Number(match, "year"),
                Number(match, "month"),
                Number(match, "day"),
                hasTime ? Number(match, "hour") : 0,
                hasTime ? Number(match, "minute") : 0,
                hasSecond ? Number(match, "second") : 0,
                millisecond,
                DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var zone = hasTime && match.Groups["zone"].Success ? match.Groups["zone"].Value : null;
        if (zone is "Z")
        {
            return new DateCandidate(wallClock, precision, origin, isUtc: true);
        }

        TimeSpan? offset = zone is not null && UtcOffsetParser.TryParse(zone, out var parsed) ? parsed : null;
        return new DateCandidate(wallClock, precision, origin, offset);
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
