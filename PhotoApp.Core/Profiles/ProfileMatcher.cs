using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// Reconnaît la source d'un fichier, dans cet ordre : motif de nom, puis marque, puis indice de dossier.
/// Le nom est d'abord lu tel quel ; les suffixes de copie ne sont retirés qu'en cas d'échec (drapeau « dérivé »).
/// </summary>
public static class ProfileMatcher
{
    // « -min » (compression), « (1) » / « 1 (2) », « - Copie », « - Copy (2) ».
    private static readonly Regex CopySuffix = new(
        @"(?:-min|\s?\(\d+\)|\s-\s(?:Copie|Copy)(?:\s\(\d+\))?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static NameReading Read(string fileName, string? make, string? folderName, IReadOnlyList<SourceProfile>? profiles = null)
    {
        profiles ??= SourceProfiles.All;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var stripped = StripCopySuffixes(stem);
        var isDerivative = stripped.Length != stem.Length;

        var byName = ReadByPattern(stem, make, profiles, isDerivative: false)
                     ?? (isDerivative ? ReadByPattern(stripped, make, profiles, isDerivative: true) : null);
        if (byName is not null)
        {
            return byName;
        }

        var profile = profiles.FirstOrDefault(p => make is not null && p.MakePattern?.IsMatch(make) == true)
                      ?? profiles.FirstOrDefault(p => folderName is not null && p.FolderPattern?.IsMatch(folderName) == true);
        return GenericNameParser.Parse(stripped, isDerivative) with { Profile = profile };
    }

    public static string StripCopySuffixes(string stem)
    {
        var current = stem;
        while (true)
        {
            var next = CopySuffix.Replace(current, string.Empty);
            if (next.Length == current.Length || next.Length == 0)
            {
                return current;
            }

            current = next;
        }
    }

    private static NameReading? ReadByPattern(string stem, string? make, IReadOnlyList<SourceProfile> profiles, bool isDerivative)
    {
        foreach (var profile in profiles)
        {
            if (profile.NameNeedsMake && (make is null || profile.MakePattern?.IsMatch(make) != true))
            {
                continue;
            }

            for (var index = 0; index < profile.NamePatterns.Count; index++)
            {
                var match = profile.NamePatterns[index].Match(stem);
                if (match.Success)
                {
                    return NamePatternReader.Read(match, profile, $"{profile.Id}#{index}", isDerivative);
                }
            }
        }

        return null;
    }
}
