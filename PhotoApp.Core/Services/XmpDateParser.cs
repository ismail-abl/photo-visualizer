using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>Lit une date XMP en conservant l'heure écrite : aucune conversion en UTC.</summary>
public static class XmpDateParser
{
    private static readonly Regex Pattern = new(
        "^(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})(?:[T ](?<hour>\\d{2}):(?<minute>\\d{2})(?::(?<second>\\d{2}))?(?:[.,]\\d+)?)?\\s*(?<zone>Z|[+-]\\d{2}:?\\d{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ImageDateCandidate? Parse(string? text)
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
        try
        {
            var wallClock = new DateTime(
                Number(match, "year"),
                Number(match, "month"),
                Number(match, "day"),
                hasTime ? Number(match, "hour") : 0,
                hasTime ? Number(match, "minute") : 0,
                hasTime && match.Groups["second"].Success ? Number(match, "second") : 0,
                DateTimeKind.Unspecified);

            TimeSpan? offset = null;
            if (hasTime && match.Groups["zone"].Success && UtcOffsetParser.TryParse(match.Groups["zone"].Value, out var parsed))
            {
                offset = parsed;
            }

            return new ImageDateCandidate(wallClock, hasTime, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
