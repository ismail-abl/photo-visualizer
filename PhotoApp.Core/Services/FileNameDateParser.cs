using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public class FileNameDateParser
{
    private static readonly Regex[] TimePatterns =
    [
        new("(?<!\\d)(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})[_-](?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("(?<!\\d)(?<day>\\d{2})[.-](?<month>\\d{2})[.-](?<year>\\d{4})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("(?<!\\d)(?<year>\\d{2})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})(?:[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2}))?(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    private static readonly Regex DateOnlyPattern = new("(?<!\\d)(?<date>\\d{8})(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public virtual ImageDateCandidate? Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var parsedWithTime = TryParseTimePatterns(fileName);
        if (parsedWithTime is not null)
        {
            return parsedWithTime;
        }

        var dateOnlyMatch = DateOnlyPattern.Match(fileName);
        if (!dateOnlyMatch.Success)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
            dateOnlyMatch.Groups["date"].Value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dateOnly))
        {
            return null;
        }

        return new ImageDateCandidate(DateTime.SpecifyKind(dateOnly, DateTimeKind.Unspecified), false);
    }

    private static ImageDateCandidate? TryParseTimePatterns(string fileName)
    {
        foreach (var pattern in TimePatterns)
        {
            var match = pattern.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            if (TryBuildDate(match, out var wallClock))
            {
                return new ImageDateCandidate(wallClock, true);
            }
        }

        return null;
    }

    private static bool TryBuildDate(Match match, out DateTime wallClock)
    {
        var yearText = match.Groups["year"].Value;
        var monthText = match.Groups["month"].Value;
        var dayText = match.Groups["day"].Value;
        var hourText = match.Groups["hour"].Success ? match.Groups["hour"].Value : "00";
        var minuteText = match.Groups["minute"].Success ? match.Groups["minute"].Value : "00";
        var secondText = match.Groups["second"].Success ? match.Groups["second"].Value : "00";

        if (!TryNormalizeYear(yearText, out var year) ||
            !int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out var minute) ||
            !int.TryParse(secondText, NumberStyles.None, CultureInfo.InvariantCulture, out var second))
        {
            wallClock = default;
            return false;
        }

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

    private static bool TryNormalizeYear(string yearText, out int year)
    {
        if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out year))
        {
            return false;
        }

        if (yearText.Length == 2)
        {
            year += 2000;
        }

        return year is >= 1 and <= 9999;
    }
}
