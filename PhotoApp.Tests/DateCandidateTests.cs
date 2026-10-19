using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class DateCandidateTests
{
    [Fact]
    public void WallClock_IsAlwaysUnspecified()
    {
        var candidate = new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12, DateTimeKind.Utc), DatePrecision.Second);

        Assert.Equal(DateTimeKind.Unspecified, candidate.WallClock.Kind);
    }

    [Fact]
    public void Utc_IgnoresAnyOffset_AndHasAKnownInstant()
    {
        var candidate = new DateCandidate(
            new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, TimeSpan.FromHours(3), isUtc: true);

        Assert.Null(candidate.Offset);
        Assert.True(candidate.HasKnownInstant);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 14, 0, 0, TimeSpan.Zero), candidate.ToInstant(TestZones.PlusOne));
    }

    [Fact]
    public void FloatingLocal_HasNoKnownInstant_AndUsesTheFallbackZone()
    {
        var candidate = new DateCandidate(new DateTime(2024, 1, 5, 15, 0, 0), DatePrecision.Second);

        Assert.False(candidate.HasKnownInstant);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 15, 0, 0, TimeSpan.FromHours(1)), candidate.ToInstant(TestZones.PlusOne));
    }

    [Fact]
    public void Localize_ConvertsUtcToTheZoneWallClock_AndKeepsTheOrigin()
    {
        var utc = new DateCandidate(new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);

        var local = utc.Localize(TestZones.PlusOne);

        Assert.False(local.IsUtc);
        Assert.Equal(new DateTime(2024, 1, 5, 15, 0, 0), local.WallClock);
        Assert.Equal(TimeSpan.FromHours(1), local.Offset);
        Assert.Equal(CandidateOrigin.Gps, local.Origin);
    }

    [Fact]
    public void Localize_LeavesALocalDateUnchanged()
    {
        var local = new DateCandidate(new DateTime(2024, 1, 5, 15, 0, 0), DatePrecision.Second);

        Assert.Same(local, local.Localize(TestZones.PlusOne));
    }

    [Theory]
    [InlineData(DatePrecision.Day, false)]
    [InlineData(DatePrecision.Minute, true)]
    [InlineData(DatePrecision.SubSecond, true)]
    public void HasTime_DependsOnThePrecision(DatePrecision precision, bool expected)
    {
        Assert.Equal(expected, new DateCandidate(new DateTime(2024, 1, 5), precision).HasTime);
    }

    [Theory]
    [InlineData("5", 500)]
    [InlineData("123", 123)]
    [InlineData("123456", 123)]
    [InlineData("07", 70)]
    public void SubSeconds_AreReadAsMilliseconds(string digits, int expected)
    {
        Assert.Equal(expected, PhotoApp.Core.Services.SubSeconds.ToMilliseconds(digits));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12a")]
    public void SubSeconds_RejectNonDigits(string? digits)
    {
        Assert.Null(PhotoApp.Core.Services.SubSeconds.ToMilliseconds(digits));
    }
}
