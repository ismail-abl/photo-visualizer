using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class FileNameDateParserTests
{
    [Theory]
    [InlineData("Persona 5 Royal 15.01.2025 00_54_14.jpg", 2025, 1, 15, 0, 54, 14, true)]
    [InlineData("Capture d’écran du 2026-03-27 12-01-17.png", 2026, 3, 27, 12, 1, 17, true)]
    [InlineData("Screenshot_20241016_172511 1.jpg", 2024, 10, 16, 17, 25, 11, true)]
    [InlineData("20260326_144546.jpg", 2026, 3, 26, 14, 45, 46, true)]
    [InlineData("IMG_20230310_184639.jpg", 2023, 3, 10, 18, 46, 39, true)]
    [InlineData("20260411_001511.jpg", 2026, 4, 11, 0, 15, 11, true)]
    [InlineData("Screenshot_20260318_035813_Firefox.jpg", 2026, 3, 18, 3, 58, 13, true)]
    [InlineData("24-03-02 13-53-44 0006.jpg", 2024, 3, 2, 13, 53, 44, true)]
    [InlineData("IMG_20230818_075449.jpg", 2023, 8, 18, 7, 54, 49, true)]
    [InlineData("IMG-20211029-WA0039.jpg", 2021, 10, 29, 0, 0, 0, false)]
    public void Parse_RecognizesSupportedPatterns(
        string fileName,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        bool hasTime)
    {
        var parser = new FileNameDateParser();

        var result = parser.Parse(fileName);

        Assert.NotNull(result);
        Assert.Equal(hasTime, result.HasTime);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), result.WallClock);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenPatternIsNotRecognized()
    {
        var parser = new FileNameDateParser();

        var result = parser.Parse("random-file-name.jpg");

        Assert.Null(result);
    }
}
