using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>Filet pour les noms qu'aucun profil ne reconnaît : une date est cherchée n'importe où dans le nom.</summary>
public static class GenericNameParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Du plus précis au moins précis ; le premier motif qui donne une date (ou une ambiguïté) l'emporte.
    private static readonly Regex[] Patterns =
    [
        // 20230304_132015 / 20230304-132015123
        new(@"(?<!\d)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})[_-](?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?<fraction>\d{3})?(?!\d)", Options),
        // 2019-05-01 10-11-12 / 2019.05.01_10.11.12 / 2019-05-01T10:11:12
        new(@"(?<!\d)(?<year>\d{4})[-_.](?<month>\d{2})[-_.](?<day>\d{2})[ T_-](?<hour>\d{2})[-_.:](?<minute>\d{2})[-_.:](?<second>\d{2})(?!\d)", Options),
        // 2019-05-01 101112
        new(@"(?<!\d)(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})[ _](?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?!\d)", Options),
        // 15.01.2025 00_54_14 (JJ.MM) ou 12.25.2024 10-30-00 (MM.JJ)
        new(@"(?<!\d)(?<d1>\d{2})[.-](?<d2>\d{2})[.-](?<year>\d{4})[ T_-](?<hour>\d{2})[-_.](?<minute>\d{2})[-_.](?<second>\d{2})(?!\d)", Options),
        // 15.01.2025 (jour seul, JJ.MM ou MM.JJ)
        new(@"(?<!\d)(?<d1>\d{2})[.-](?<d2>\d{2})[.-](?<year>\d{4})(?!\d)", Options),
        // 2019-05-01 (jour seul)
        new(@"(?<!\d)(?<year>\d{4})[-_.](?<month>\d{2})[-_.](?<day>\d{2})(?!\d)", Options),
        // 20190501 (jour seul)
        new(@"(?<!\d)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?!\d)", Options)
    ];

    public static NameReading Parse(string stem, bool isDerivative = false)
    {
        for (var index = 0; index < Patterns.Length; index++)
        {
            foreach (Match match in Patterns[index].Matches(stem))
            {
                var reading = NamePatternReader.Read(match, profile: null, $"generique#{index}", isDerivative);
                if (reading.Date is not null || reading.Ambiguous is not null)
                {
                    return reading;
                }
            }
        }

        return NameReading.None with { IsDerivative = isDerivative };
    }
}
