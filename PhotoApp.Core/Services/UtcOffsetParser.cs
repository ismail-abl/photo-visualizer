using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Services;

public static class UtcOffsetParser
{
    private static readonly Regex Pattern = new(
        "^(?<sign>[+-])(?<hours>\\d{2}):?(?<minutes>\\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out TimeSpan offset)
    {
        offset = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed is "Z" or "z")
        {
            offset = TimeSpan.Zero;
            return true;
        }

        var match = Pattern.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        if (hours > 14 || minutes > 59)
        {
            return false;
        }

        var value = new TimeSpan(hours, minutes, 0);
        offset = match.Groups["sign"].Value == "-" ? -value : value;
        return true;
    }
}
