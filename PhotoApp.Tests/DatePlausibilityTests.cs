using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class DatePlausibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    [Theory]
    [InlineData(2023, 3, 4, true)]
    [InlineData(1990, 1, 1, true)]
    [InlineData(1989, 12, 31, false)]
    [InlineData(2026, 10, 19, true)]   // demain : toléré
    [InlineData(2026, 10, 20, false)]  // après-demain : refusé
    public void IsPlausible_AppliesTheWindow(int year, int month, int day, bool expected)
    {
        var candidate = new ImageDateCandidate(new DateTime(year, month, day, 10, 0, 0), true);

        Assert.Equal(expected, DatePlausibility.IsPlausible(candidate, Now));
    }

    [Theory]
    [InlineData(2000, 1, 1)]
    [InlineData(1970, 1, 1)]
    public void IsPlausible_RejectsResetClocks(int year, int month, int day)
    {
        var candidate = new ImageDateCandidate(new DateTime(year, month, day, 0, 0, 0), true);

        Assert.False(DatePlausibility.IsPlausible(candidate, Now));
    }
}
