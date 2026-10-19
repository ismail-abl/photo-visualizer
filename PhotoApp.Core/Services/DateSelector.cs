using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class DateSelector
{
    public static (DateCandidate? Candidate, ImageDateSource Source) Select(
        DateCandidate? native,
        DateCandidate? fileName,
        DateCandidate? fileSystem,
        DateTime utcNow)
    {
        if (IsUsable(native, utcNow))
        {
            return (native, ImageDateSource.NativeMetadata);
        }

        if (IsUsable(fileName, utcNow))
        {
            return (fileName, ImageDateSource.FileName);
        }

        if (IsUsable(fileSystem, utcNow))
        {
            return (fileSystem, ImageDateSource.FileSystem);
        }

        return (null, ImageDateSource.None);
    }

    /// <summary>La date de modification du fichier est la date de copie : jamais fiable.</summary>
    public static bool IsReliable(ImageDateSource source) =>
        source is ImageDateSource.NativeMetadata or ImageDateSource.FileName;

    private static bool IsUsable(DateCandidate? candidate, DateTime utcNow) =>
        candidate is not null && DatePlausibility.IsPlausible(candidate, utcNow);
}
