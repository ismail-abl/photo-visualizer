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
    [InlineData("Screenshot 2025-08-27 211650.jpg", 2025, 8, 27, 21, 16, 50, true)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", 2024, 1, 5, 15, 4, 12, true)]
    [InlineData("Screenshot 2024-01-05 at 12.04.12 AM.png", 2024, 1, 5, 0, 4, 12, true)]
    [InlineData("Capture d’écran 2024-01-05 à 15.04.12.png", 2024, 1, 5, 15, 4, 12, true)]
    [InlineData("WhatsApp Image 2023-05-01 at 14.03.22.jpeg", 2023, 5, 1, 14, 3, 22, true)]
    [InlineData("PXL_20230304_132015123.jpg", 2023, 3, 4, 13, 20, 15, true)]
    [InlineData("signal-2023-12-18-211842_002.jpg", 2023, 12, 18, 0, 0, 0, false)] // heure Signal : profils, phase 1
    [InlineData("12.25.2024 10-30-00.jpg", 2024, 12, 25, 10, 30, 0, true)]
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

    [Theory]
    [InlineData("mmexport1700000000000.jpg", 2023, 11, 14, 22, 13, 20)]
    [InlineData("FB_IMG_1700000000.jpg", 2023, 11, 14, 22, 13, 20)]
    public void Parse_EpochNames_AreUtc(string fileName, int year, int month, int day, int hour, int minute, int second)
    {
        var result = new FileNameDateParser().Parse(fileName);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), result.WallClock);
        Assert.True(result.IsUtc);
        Assert.True(result.HasTime);
    }

    [Theory]
    [InlineData("03-04-2025 10-00-00.jpg")]   // JJ-MM ou MM-JJ : indécidable sans autre indice
    [InlineData("random-file-name.jpg")]
    [InlineData("IMG_20991231_235959.jpg")]   // dans le futur
    [InlineData("IMG_19800101_000001.jpg")]   // avant 1990
    public void Parse_ReturnsNull_ForAmbiguousOrImplausibleNames(string fileName)
    {
        Assert.Null(new FileNameDateParser().Parse(fileName));
    }
}
