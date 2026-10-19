using System.Globalization;

namespace PhotoApp.Core.Services;

public static class SubSeconds
{
    /// <summary>Fraction de seconde écrite en chiffres : « 5 » → 500 ms, « 123456 » → 123 ms. Null si ce n'est pas une suite de chiffres.</summary>
    public static int? ToMilliseconds(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return int.Parse(digits.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
    }
}
