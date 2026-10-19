using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class XmpDateParserTests
{
    [Fact]
    public void Parse_KeepsWallClockAndOffset()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15+09:00");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
        Assert.True(result.HasTime);
    }

    [Fact]
    public void Parse_WithoutZone_IsFloating()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Null(result.Offset);
    }

    [Fact]
    public void Parse_Z_IsUtc_WithoutConvertingTheClock()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15Z");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.True(result.IsUtc);
        Assert.Null(result.Offset);
    }

    [Fact]
    public void Parse_ReportsPrecisionAndOrigin()
    {
        Assert.Equal(DatePrecision.Second, XmpDateParser.Parse("2023-03-04T13:20:15")!.Precision);
        Assert.Equal(DatePrecision.Minute, XmpDateParser.Parse("2023-03-04T13:20")!.Precision);
        Assert.Equal(DatePrecision.Day, XmpDateParser.Parse("2023-03-04")!.Precision);
        Assert.Equal(CandidateOrigin.Xmp, XmpDateParser.Parse("2023-03-04")!.Origin);
        Assert.Equal(CandidateOrigin.QuickTimeApple, XmpDateParser.Parse("2024-07-11T17:07:10+0900", CandidateOrigin.QuickTimeApple)!.Origin);
    }

    [Fact]
    public void Parse_Fraction_IsSubSecond()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15.25+01:00");

        Assert.NotNull(result);
        Assert.Equal(DatePrecision.SubSecond, result.Precision);
        Assert.Equal(250, result.WallClock.Millisecond);
        Assert.Equal(TimeSpan.FromHours(1), result.Offset);
    }

    [Fact]
    public void Parse_AppleOffsetWithoutColon_IsRead()
    {
        var result = XmpDateParser.Parse("2024-07-11T17:07:10+0900");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), result.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
    }

    [Fact]
    public void Parse_DateOnly_HasNoTimeAndNoOffset()
    {
        var result = XmpDateParser.Parse("2023-03-04");

        Assert.NotNull(result);
        Assert.False(result.HasTime);
        Assert.Null(result.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("2023-13-40T00:00:00")]
    public void Parse_ReturnsNull_ForInvalidText(string? text)
    {
        Assert.Null(XmpDateParser.Parse(text));
    }
}
