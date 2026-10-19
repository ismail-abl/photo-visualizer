using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class DateComparisonTests
{
    private static DateCandidate Local(int h, int m, int s, TimeSpan? offset = null, DatePrecision precision = DatePrecision.Second, int day = 5) =>
        new(new DateTime(2024, 1, day, h, m, s), precision, CandidateOrigin.ExifOriginal, offset);

    private static DateCandidate Utc(int h, int m, int s, int day = 5) =>
        new(new DateTime(2024, 1, day, h, m, s), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

    [Theory]
    [InlineData(15, 4, 12, true)]
    [InlineData(15, 4, 14, true)]    // 2 s : toléré
    [InlineData(15, 4, 15, false)]   // 3 s : refusé
    public void Concordant_Seconds_WithinTwoSeconds(int h, int m, int s, bool expected)
    {
        Assert.Equal(expected, DateComparison.Concordant(Local(15, 4, 12), Local(h, m, s)));
    }

    [Fact]
    public void Concordant_ComparesAtTheWeakerPrecision()
    {
        var day = Local(0, 0, 0, precision: DatePrecision.Day);

        Assert.True(DateComparison.Concordant(day, Local(23, 59, 59)));
        Assert.False(DateComparison.Concordant(day, Local(10, 0, 0, day: 6)));
        Assert.True(DateComparison.Concordant(Local(15, 4, 0, precision: DatePrecision.Minute), Local(15, 4, 59)));
    }

    [Fact]
    public void Concordant_TwoKnownInstants_AreComparedAsInstants()
    {
        Assert.True(DateComparison.Concordant(Local(10, 0, 0, TimeSpan.FromHours(2)), Local(9, 0, 0, TimeSpan.FromHours(1))));
        Assert.True(DateComparison.Concordant(Local(10, 0, 0, TimeSpan.FromHours(2)), Utc(8, 0, 0)));
    }

    [Fact]
    public void Concordant_FloatingLocalAgainstUtc_IsNotDecidedHere()
    {
        Assert.False(DateComparison.Concordant(Local(10, 0, 0), Utc(10, 0, 0)));
    }

    [Theory]
    [InlineData(22, 29, 44, 2.0)]     // Samsung : nom local, mvhd UTC
    [InlineData(22, 29, 45, 2.0)]     // 1 s d'écart : toléré
    [InlineData(20, 29, 44, 0.0)]     // écart nul : fuseau UTC+0
    [InlineData(1, 59, 44, 5.5)]      // Inde : +05:30 (le lendemain)
    public void ZoneGap_IsAMultipleOfAQuarterHour(int h, int m, int s, double expectedHours)
    {
        var local = new DateCandidate(new DateTime(2026, 9, h < 12 ? 18 : 17, h, m, s), DatePrecision.Second, CandidateOrigin.FileName);
        var utc = new DateCandidate(new DateTime(2026, 9, 17, 20, 29, 44), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        Assert.Equal(TimeSpan.FromHours(expectedHours), DateComparison.ZoneGap(local, utc));
    }

    [Fact]
    public void ZoneGap_IsRefused_WhenNotAQuarterHour_TooLarge_OrTooCoarse()
    {
        Assert.Null(DateComparison.ZoneGap(Local(22, 36, 44), Utc(20, 29, 44)));            // 7 min de trop
        Assert.Null(DateComparison.ZoneGap(Local(12, 0, 0, day: 6), Utc(20, 0, 0)));        // 16 h
        Assert.Null(DateComparison.ZoneGap(Local(0, 0, 0, precision: DatePrecision.Day), Utc(20, 0, 0)));
        Assert.Null(DateComparison.ZoneGap(Utc(20, 0, 0), Utc(20, 0, 0)));                  // il faut une heure locale
    }

    [Fact]
    public void ZoneGap_IsRefused_WhenTheLocalValueCarriesAnOffset()
    {
        Assert.Null(DateComparison.ZoneGap(Local(17, 7, 10, TimeSpan.FromHours(2)), Utc(12, 7, 10)));   // heure locale sans décalage seulement
    }

    [Fact]
    public void OffsetFromGps_RoundsToAQuarterHour_WithinTwoMinutes()
    {
        var exif = Local(13, 20, 15);
        var gps = new DateCandidate(new DateTime(2024, 1, 5, 12, 20, 14), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);
        var staleGps = new DateCandidate(new DateTime(2024, 1, 5, 12, 17, 0), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);

        Assert.Equal(TimeSpan.FromHours(1), DateComparison.OffsetFromGps(exif, gps));
        Assert.Null(DateComparison.OffsetFromGps(exif, staleGps));   // 3 min 15 s de reste
    }

    [Fact]
    public void CompareAt_TruncatesToThePrecision()
    {
        var morning = Local(8, 0, 0);
        var evening = Local(20, 0, 0);

        Assert.Equal(0, DateComparison.CompareAt(morning, evening, DatePrecision.Day));
        Assert.True(DateComparison.CompareAt(morning, evening, DatePrecision.Second) < 0);
        Assert.True(DateComparison.CompareAt(Local(8, 0, 0, day: 6), evening, DatePrecision.Day) > 0);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(-2, true)]
    [InlineData(-3, false)]
    public void ExifMatchesName_AllowsTheWriteLatency(int exifMinusNameSeconds, bool expected)
    {
        var name = new DateCandidate(new DateTime(2026, 10, 12, 1, 49, 32), DatePrecision.Second, CandidateOrigin.FileName);
        var exif = new DateCandidate(new DateTime(2026, 10, 12, 1, 49, 32).AddSeconds(exifMinusNameSeconds), DatePrecision.Second, CandidateOrigin.ExifOriginal);

        Assert.Equal(expected, DateComparison.ExifMatchesName(exif, name));
    }

    [Fact]
    public void ZoneGap_MovieHeaderMayBeWrittenAtTheEndOfTheRecording()
    {
        var start = new DateCandidate(new DateTime(2023, 10, 27, 19, 52, 14), DatePrecision.Second, CandidateOrigin.FileName);
        DateCandidate Header(int h, int m, int s) => new(new DateTime(2023, 10, 27, h, m, s), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        Assert.Equal(TimeSpan.FromHours(2), DateComparison.ZoneGap(start, Header(17, 52, 46)));   // 32 s de vidéo
        Assert.Null(DateComparison.ZoneGap(start, Header(18, 0, 30)));                            // 8 min 16 s : trop long
        Assert.Null(DateComparison.ZoneGap(start, Header(17, 52, 10)));                           // en-tête 4 s AVANT le début
    }

    [Fact]
    public void Concordant_AppleDateAndALaterMovieHeader()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 8, 14, 4, 59), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        DateCandidate Header(int h, int m, int s) => new(new DateTime(2024, 7, 8, h, m, s), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        Assert.True(DateComparison.Concordant(apple, Header(12, 5, 18)));
        Assert.False(DateComparison.Concordant(apple, Header(12, 13, 0)));
        Assert.False(DateComparison.Concordant(apple, Header(12, 4, 55)));
    }

    [Fact]
    public void Concordant_DayAgainstUtc_AcceptsEveryTimeZone()
    {
        var day = new DateCandidate(new DateTime(2023, 9, 1), DatePrecision.Day, CandidateOrigin.FileName);

        Assert.True(DateComparison.Concordant(day, new DateCandidate(new DateTime(2023, 9, 1, 21, 34, 16), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true)));
        Assert.True(DateComparison.Concordant(day, new DateCandidate(new DateTime(2023, 8, 31, 11, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true)));   // J - 13 h
        Assert.False(DateComparison.Concordant(day, new DateCandidate(new DateTime(2023, 9, 3, 10, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true)));
    }
}
