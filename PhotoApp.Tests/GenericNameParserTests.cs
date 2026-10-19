using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class GenericNameParserTests
{
    [Theory]
    [InlineData("Persona 5 Royal 15.01.2025 00_54_14", 2025, 1, 15, 0, 54, 14)]   // JJ.MM : 15 > 12
    [InlineData("12.25.2024 10-30-00", 2024, 12, 25, 10, 30, 0)]                  // MM.JJ : 25 > 12
    [InlineData("vacances 2019-05-01 10-11-12", 2019, 5, 1, 10, 11, 12)]
    [InlineData("export_20230304-132015", 2023, 3, 4, 13, 20, 15)]
    [InlineData("scan 2019-05-01 101112", 2019, 5, 1, 10, 11, 12)]
    public void Parse_DatesWithTime(string stem, int year, int month, int day, int hour, int minute, int second)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), reading.Date.WallClock);
        Assert.Equal(DatePrecision.Second, reading.Date.Precision);
        Assert.Equal(CandidateOrigin.FileName, reading.Date.Origin);
        Assert.False(reading.FromProfilePattern);
        Assert.Null(reading.Profile);
    }

    [Theory]
    [InlineData("scan 20190501")]
    [InlineData("anniversaire 2019-05-01")]
    public void Parse_DayOnly(string stem)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(2019, 5, 1), reading.Date.WallClock);
        Assert.Equal(DatePrecision.Day, reading.Date.Precision);
    }

    [Fact]
    public void Parse_AmbiguousDayMonth_KeepsBothReadings()
    {
        var reading = GenericNameParser.Parse("03-04-2025 10-00-00");

        Assert.Null(reading.Date);
        Assert.NotNull(reading.Ambiguous);
        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), reading.Ambiguous.DayFirst.WallClock);
        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), reading.Ambiguous.MonthFirst.WallClock);
    }

    [Fact]
    public void Parse_UnambiguousDayMonth_GivesEvidenceOfTheOrder()
    {
        var dayFirst = GenericNameParser.Parse("25-12-2024 10-00-00");
        var ambiguous = GenericNameParser.Parse("03-04-2025 10-00-00");

        Assert.Equal(new DayMonthEvidence(ambiguous.Ambiguous!.PatternKey, DayMonthOrder.DayFirst), dayFirst.Evidence);
    }

    [Fact]
    public void Parse_SameDayAndMonth_IsNotAmbiguous()
    {
        var reading = GenericNameParser.Parse("04-04-2025 10-00-00");

        Assert.Equal(new DateTime(2025, 4, 4, 10, 0, 0), reading.Date?.WallClock);
        Assert.Null(reading.Ambiguous);
    }

    [Theory]
    [InlineData("random-file-name")]
    [InlineData("IMG_12345678")]       // 1234-56-78 : pas une date
    [InlineData("")]
    public void Parse_NoDate(string stem)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.Null(reading.Date);
        Assert.Null(reading.Ambiguous);
    }

    [Fact]
    public void Parse_KeepsImplausibleDates_ForTheArbiterToReject()
    {
        Assert.Equal(new DateTime(2099, 12, 31, 23, 59, 59), GenericNameParser.Parse("x_20991231_235959").Date?.WallClock);
    }

    [Fact]
    public void Parse_CarriesTheDerivativeFlag()
    {
        Assert.True(GenericNameParser.Parse("photo", isDerivative: true).IsDerivative);
    }
}
