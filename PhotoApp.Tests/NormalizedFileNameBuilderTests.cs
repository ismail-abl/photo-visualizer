using System.IO;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class NormalizedFileNameBuilderTests
{
    [Theory]
    [InlineData("Persona 5 Royal 15.01.2025 00_54_14.jpg", 2025, 1, 15, 0, 54, 14, true, "IMG-20250115-005414.jpg")]
    [InlineData("Capture d’écran du 2026-03-27 12-01-17.png", 2026, 3, 27, 12, 1, 17, true, "IMG-20260327-120117.png")]
    [InlineData("Screenshot_20241016_172511 1.jpg", 2024, 10, 16, 17, 25, 11, true, "IMG-20241016-172511.jpg")]
    [InlineData("20260326_144546.jpg", 2026, 3, 26, 14, 45, 46, true, "IMG-20260326-144546.jpg")]
    [InlineData("IMG_20230310_184639.jpg", 2023, 3, 10, 18, 46, 39, true, "IMG-20230310-184639.jpg")]
    [InlineData("20260411_001511.jpg", 2026, 4, 11, 0, 15, 11, true, "IMG-20260411-001511.jpg")]
    [InlineData("Screenshot_20260318_035813_Firefox.jpg", 2026, 3, 18, 3, 58, 13, true, "IMG-20260318-035813.jpg")]
    [InlineData("24-03-02 13-53-44 0006.jpg", 2024, 3, 2, 13, 53, 44, true, "IMG-20240302-135344.jpg")]
    [InlineData("IMG_20230818_075449.jpg", 2023, 8, 18, 7, 54, 49, true, "IMG-20230818-075449.jpg")]
    [InlineData("IMG-20211029-WA0039.jpg", 2021, 10, 29, 0, 0, 0, false, "IMG-20211029-58ebe3.jpg")] // hash of "IMG-20211029-WA0039.jpg"
    public void Build_CreatesNormalizedFileName(
        string filename,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        bool hasTime,
        string expected)
    {
        var date = new ImageDateCandidate(new DateTime(year, month, day, hour, minute, second), hasTime);

        var result = NormalizedFileNameBuilder.Build(date, Path.GetExtension(filename), filename);

        Assert.Equal(expected, result);
    }
}