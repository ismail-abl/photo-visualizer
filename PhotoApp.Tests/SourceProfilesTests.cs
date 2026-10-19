using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class SourceProfilesTests
{
    private static DateTime ToSecond(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);

    [Theory]
    // Messageries
    [InlineData("IMG-20211029-WA0039.jpg", null, "whatsapp", 2021, 10, 29, 0, 0, 0, DatePrecision.Day)]
    [InlineData("VID-20151114-WA0017.mp4", null, "whatsapp", 2015, 11, 14, 0, 0, 0, DatePrecision.Day)]
    [InlineData("WhatsApp Image 2023-05-01 at 14.03.22.jpeg", null, "whatsapp", 2023, 5, 1, 14, 3, 22, DatePrecision.Second)]
    [InlineData("WhatsApp Image 2023-05-01 at 2.03.22 PM.jpeg", null, "whatsapp", 2023, 5, 1, 14, 3, 22, DatePrecision.Second)]
    [InlineData("signal-2023-12-18-211842_002.jpg", null, "signal", 2023, 12, 18, 21, 18, 42, DatePrecision.Second)]
    [InlineData("signal-2024-02-01-10-11-12-345.jpg", null, "signal", 2024, 2, 1, 10, 11, 12, DatePrecision.SubSecond)]
    [InlineData("photo_2024-01-05_15-04-12.jpg", null, "telegram", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    // Captures d'écran
    [InlineData("Screenshot_20240410_111652.jpg", null, "capture-android-kde", 2024, 4, 10, 11, 16, 52, DatePrecision.Second)]
    [InlineData("Screenshot_20260318_035813_Firefox.jpg", null, "capture-android-kde", 2026, 3, 18, 3, 58, 13, DatePrecision.Second)]
    [InlineData("Screenshot_20241016_172511 1.jpg", null, "capture-android-kde", 2024, 10, 16, 17, 25, 11, DatePrecision.Second)]
    [InlineData("Screenshot_2019-05-01-10-11-12.png", null, "capture-android-kde", 2019, 5, 1, 10, 11, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2025-08-27 211650.jpg", null, "capture-windows", 2025, 8, 27, 21, 16, 50, DatePrecision.Second)]
    [InlineData("Capture d’écran 2025-08-27 211650.png", null, "capture-windows", 2025, 8, 27, 21, 16, 50, DatePrecision.Second)]
    [InlineData("Capture d’écran du 2026-03-27 01-22-28.png", null, "capture-gnome", 2026, 3, 27, 1, 22, 28, DatePrecision.Second)]
    [InlineData("Capture d'écran du 2026-03-27 12-01-17.png", null, "capture-gnome", 2026, 3, 27, 12, 1, 17, DatePrecision.Second)]
    [InlineData("Screenshot from 2023-02-03 04-05-06.png", null, "capture-gnome", 2023, 2, 3, 4, 5, 6, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 12.04.12 AM.png", null, "capture-macos", 2024, 1, 5, 0, 4, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    [InlineData("Capture d’écran 2024-01-05 à 15.04.12.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    // Appareils
    [InlineData("IMG_20230304_132015.jpg", "Xiaomi", "xiaomi", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "HUAWEI", "huawei", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "OnePlus", "oneplus", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "Canon", "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("VID_20230304_132015.mp4", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("MVIMG_20230304_132015.jpg", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("20260917_222944.heic", "samsung", "samsung", 2026, 9, 17, 22, 29, 44, DatePrecision.Second)]
    [InlineData("20260411_001511.jpg", null, "samsung", 2026, 4, 11, 0, 15, 11, DatePrecision.Second)]
    [InlineData("24-07-11 17-07-10 0477.mov", null, "nextcloud-ios", 2024, 7, 11, 17, 7, 10, DatePrecision.Second)]
    [InlineData("PXL_20230304_122015123.jpg", null, "pixel", 2023, 3, 4, 12, 20, 15, DatePrecision.SubSecond)]
    [InlineData("PXL_20230304_122015123.MP.jpg", null, "pixel", 2023, 3, 4, 12, 20, 15, DatePrecision.SubSecond)]
    public void Read_RecognizesTheProfileAndTheDate(
        string fileName, string? make, string profileId, int year, int month, int day, int hour, int minute, int second, DatePrecision precision)
    {
        var reading = ProfileMatcher.Read(fileName, make, folderName: null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.True(reading.FromProfilePattern);
        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), ToSecond(reading.Date.WallClock));
        Assert.Equal(precision, reading.Date.Precision);
        Assert.Equal(CandidateOrigin.FileName, reading.Date.Origin);
    }

    [Theory]
    [InlineData("mmexport1700000000000.jpg", "wechat")]
    [InlineData("FB_IMG_1700000000.jpg", "facebook")]
    [InlineData("1700000000000.jpg", "epoch")]
    public void Read_EpochNames_AreUtc(string fileName, string profileId)
    {
        var reading = ProfileMatcher.Read(fileName, null, null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.True(reading.Date?.IsUtc);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20), reading.Date!.WallClock);
    }

    [Fact]
    public void Read_PixelNames_AreUtc()
    {
        Assert.True(ProfileMatcher.Read("PXL_20230304_122015123.jpg", null, null).Date?.IsUtc);
    }

    [Theory]
    [InlineData("IMG-20211029-WA0039.jpg", 39)]
    [InlineData("24-07-11 17-07-10 0477.jpg", 477)]
    [InlineData("signal-2023-12-18-211842_002.jpg", 2)]
    [InlineData("20260917_222944(1).jpg", 1)]
    [InlineData("Screenshot (12).png", 12)]
    public void Read_ExtractsTheCounter(string fileName, long counter)
    {
        Assert.Equal(counter, ProfileMatcher.Read(fileName, null, null).Counter);
    }

    [Theory]
    [InlineData("IMG_0477.HEIC", "Apple", "iphone")]
    [InlineData("hqdefault-4B744.jpg", null, "miniature-video")]
    [InlineData("Snapchat-123456789.jpg", null, "snapchat")]
    [InlineData("Screenshot (12).png", null, "capture-windows-impr-ecran")]
    public void Read_ProfilesWithoutADateInTheName(string fileName, string? make, string profileId)
    {
        var reading = ProfileMatcher.Read(fileName, make, null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.Null(reading.Date);
    }

    [Fact]
    public void Read_IPhoneNameWithoutAppleMake_IsNotRecognized()
    {
        Assert.Null(ProfileMatcher.Read("IMG_0477.HEIC", "Canon", null).Profile);
    }

    [Fact]
    public void Read_CopySuffix_IsStrippedOnlyWhenNeeded_AndFlagged()
    {
        var compressed = ProfileMatcher.Read("IMG_20230304_132015-min.jpg", null, null);
        var burst = ProfileMatcher.Read("20260917_222944(1).jpg", null, null);
        var copy = ProfileMatcher.Read("photo (1).jpg", null, null);

        Assert.Equal("android", compressed.Profile?.Id);
        Assert.True(compressed.IsDerivative);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), compressed.Date?.WallClock);
        Assert.False(burst.IsDerivative);
        Assert.True(copy.IsDerivative);
        Assert.Null(copy.Profile);
    }

    [Theory]
    [InlineData("IMG_20230304_132015-min - Copie (2)", "IMG_20230304_132015")]
    [InlineData("photo (1)", "photo")]
    [InlineData("photo", "photo")]
    public void StripCopySuffixes_RemovesEverySuffix(string stem, string expected)
    {
        Assert.Equal(expected, ProfileMatcher.StripCopySuffixes(stem));
    }

    [Fact]
    public void Read_MakeOnly_GivesTheProfile_AndTheGenericParserReadsTheName()
    {
        var album = ProfileMatcher.Read("001.jpg", "Xiaomi", null);
        var renamed = ProfileMatcher.Read("vacances 2019-05-01 10-11-12.jpg", "samsung", null);

        Assert.Equal("xiaomi", album.Profile?.Id);
        Assert.False(album.FromProfilePattern);
        Assert.Null(album.Date);
        Assert.Equal("samsung", renamed.Profile?.Id);
        Assert.False(renamed.FromProfilePattern);
        Assert.Equal(new DateTime(2019, 5, 1, 10, 11, 12), renamed.Date?.WallClock);
    }

    [Fact]
    public void Read_FolderHint_GivesTheProfile()
    {
        Assert.Equal("snapchat", ProfileMatcher.Read("image.jpg", null, "Snapchat").Profile?.Id);
    }

    [Fact]
    public void Read_UnknownName_HasNoProfileAndNoDate()
    {
        var reading = ProfileMatcher.Read("random.jpg", null, null);

        Assert.Null(reading.Profile);
        Assert.Null(reading.Date);
        Assert.False(reading.FromProfilePattern);
    }

    [Fact]
    public void Read_KeepsImplausibleDates_ForTheArbiterToReject()
    {
        Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 1), ProfileMatcher.Read("IMG_19800101_000001.jpg", null, null).Date?.WallClock);
    }

    [Fact]
    public void Read_ProfileDayMonthOrder_SettlesAnAmbiguousName()
    {
        var pattern = new Regex(@"^cam (?<d1>\d{2})\.(?<d2>\d{2})\.(?<year>\d{4})$");
        SourceProfile[] profiles =
        [
            new("cam-fr", "Caméra FR", ProfileFamily.Camera, [pattern], DayMonthOrder: DayMonthOrder.DayFirst),
        ];

        var reading = ProfileMatcher.Read("cam 03.04.2025.jpg", null, null, profiles);

        Assert.Equal(new DateTime(2025, 4, 3), reading.Date?.WallClock);
        Assert.Null(reading.Ambiguous);
    }

    [Fact]
    public void Read_GenericAmbiguousName_StaysAmbiguous()
    {
        var reading = ProfileMatcher.Read("03-04-2025 10-00-00.jpg", null, null);

        Assert.Null(reading.Date);
        Assert.NotNull(reading.Ambiguous);
    }

    [Fact]
    public void All_HasUniqueIds()
    {
        var ids = SourceProfiles.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
