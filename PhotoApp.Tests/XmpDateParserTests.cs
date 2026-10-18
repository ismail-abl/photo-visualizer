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
    public void Parse_Z_MeansZeroOffset_WithoutConvertingTheClock()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15Z");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Equal(TimeSpan.Zero, result.Offset);
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
