using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class DatePlausibility
{
    private static readonly DateTime WindowStart = new(1990, 1, 1);

    private static readonly DateTime[] ResetClocks =
    [
        new(2000, 1, 1, 0, 0, 0),
        new(1970, 1, 1, 0, 0, 0)
    ];

    public static bool IsPlausible(ImageDateCandidate candidate, DateTime utcNow)
    {
        var date = candidate.WallClock;
        if (date < WindowStart || date > utcNow.AddDays(1))
        {
            return false;
        }

        return !ResetClocks.Contains(date);
    }
}
