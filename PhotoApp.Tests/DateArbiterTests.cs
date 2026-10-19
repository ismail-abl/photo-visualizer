using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class DateArbiterTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 19, 12, 0, 0);
    private static readonly DateTime CopiedOn = new(2023, 3, 1, 9, 0, 0);   // date de modification : celle de la copie
    private static readonly DateArbiter Arbiter = new(new NormalizationOptions(UtcNow, TestZones.PlusOne));

    internal static DateCandidate Exif(DateTime value, TimeSpan? offset = null) =>
        new(value, DatePrecision.Second, CandidateOrigin.ExifOriginal, offset);

    internal static DateCandidate Utc(DateTime value, CandidateOrigin origin) =>
        new(value, DatePrecision.Second, origin, isUtc: true);

    internal static DateDecision Decide(string fileName, string? make = null, DateTime? lastWriteUtc = null, params DateCandidate[] dates) =>
        Arbiter.Decide(ProfileMatcher.Read(fileName, make, folderName: null), new RawMetadata { Dates = dates, Make = make }, lastWriteUtc ?? CopiedOn);

    [Fact]
    public void AlreadyRenamedDayName_IsCertain_AtDayPrecision()
    {
        var decision = Decide("IMG-20210915-000000-03.jpg");

        Assert.Equal(Reasons.AlreadyNormalized, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(DatePrecision.Day, decision.Date?.Precision);
    }

    [Fact]
    public void AlreadyRenamedName_WithAnExif_IsStillTheName()
    {
        var decision = Decide("IMG-20250704-160803.jpg", dates: Exif(new DateTime(2025, 7, 4, 16, 8, 3)));

        Assert.Equal(Reasons.AlreadyNormalized, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }

    [Fact]
    public void ProfileName_ConfirmingExif_StillTakesTheOffsetFromGps()
    {
        var decision = Decide("IMG_20230304_132015.jpg", "Xiaomi", dates:
        [
            Exif(new DateTime(2023, 3, 4, 13, 20, 15)),
            Utc(new DateTime(2023, 3, 4, 12, 20, 14), CandidateOrigin.Gps)
        ]);

        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(TimeSpan.FromHours(1), decision.Date?.Offset);
    }

    [Fact]
    public void DeviceName_LaterThanTheExif_ExifIsTheTruth()
    {
        var decision = Decide("IMG_20250805_170441.jpg", dates: Exif(new DateTime(2025, 8, 5, 13, 15, 16)));

        Assert.Equal(Reasons.ExifBeforeName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(new DateTime(2025, 8, 5, 13, 15, 16), decision.Date?.WallClock);
    }

    [Fact]
    public void GenericName_LaterThanTheExif_StaysExifOnly()
    {
        var decision = Decide("vacances 2019-05-01 10-11-12.jpg", dates: Exif(new DateTime(2019, 4, 1, 9, 0, 0)));

        Assert.Equal(Reasons.ExifOnly, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
    }

    [Fact]
    public void ScreenshotName_LaterThanTheExif_IsStillADisagreement()
    {
        var decision = Decide("Screenshot 2025-08-27 211650.png", dates: Exif(new DateTime(2025, 8, 1, 9, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.Equal(Confidence.Uncertain, decision.Confidence);
    }

    [Fact]
    public void WhatsApp_NameIsTheTruth_EvenWhenTheFileWasCopiedLater()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg");

        Assert.Equal(new DateTime(2021, 9, 15), decision.Date?.WallClock);
        Assert.Equal(DatePrecision.Day, decision.Date?.Precision);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.MessagingName, decision.Reason);
        Assert.True(decision.IsApplicable);
    }

    [Fact]
    public void Messaging_OlderExif_IsTheRealCapture()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2019, 5, 1, 10, 0, 0)));

        Assert.Equal(new DateTime(2019, 5, 1, 10, 0, 0), decision.Date?.WallClock);
        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.MessagingExif, decision.Reason);
    }

    [Fact]
    public void Messaging_SameDayExif_GivesTheExactTime()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2021, 9, 15, 8, 30, 0)));

        Assert.Equal(new DateTime(2021, 9, 15, 8, 30, 0), decision.Date?.WallClock);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.MessagingExif, decision.Reason);
    }

    [Fact]
    public void Screenshot_NameIsTheCaptureTime()
    {
        var decision = Decide("Screenshot 2025-08-27 211650.png");

        Assert.Equal(new DateTime(2025, 8, 27, 21, 16, 50), decision.Date?.WallClock);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ScreenshotName, decision.Reason);
    }

    [Fact]
    public void ExifWithOffset_IsCertain()
    {
        var decision = Decide("photo.jpg", dates: Exif(new DateTime(2024, 1, 5, 15, 4, 12), TimeSpan.FromHours(9)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifWithOffset, decision.Reason);
        Assert.Equal(TimeSpan.FromHours(9), decision.Date?.Offset);
    }

    [Fact]
    public void AppleQuickTimeDate_IsCertain()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var movieHeader = Utc(new DateTime(2024, 7, 11, 15, 7, 10), CandidateOrigin.QuickTimeMovieHeader);

        var decision = Decide("IMG_0477.mov", "Apple", dates: [apple, movieHeader]);

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.QuickTimeApple, decision.Reason);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), decision.Date?.WallClock);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByTheProfileName_IsCertain()
    {
        var decision = Decide("IMG_20230304_132015.jpg", "Xiaomi", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByGps_GetsTheDeducedOffset()
    {
        var gps = Utc(new DateTime(2023, 3, 4, 12, 20, 14), CandidateOrigin.Gps);

        var decision = Decide("001.jpg", dates: [Exif(new DateTime(2023, 3, 4, 13, 20, 15)), gps]);

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
        Assert.Equal(TimeSpan.FromHours(1), decision.Date?.Offset);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByAUtcPixelName_GetsTheDeducedOffset()
    {
        var decision = Decide("PXL_20230304_122015123.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(TimeSpan.FromHours(1), decision.Date?.Offset);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), decision.Date?.WallClock);
    }

    [Fact]
    public void ExifAlone_IsProbable()
    {
        var decision = Decide("001.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.ExifOnly, decision.Reason);
    }

    [Fact]
    public void VideoUtcHeader_AndLocalProfileName_GiveTheWallClockAndTheOffset()
    {
        var decision = Decide("20260917_222944.mp4", dates: Utc(new DateTime(2026, 9, 17, 20, 29, 44), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.VideoUtcName, decision.Reason);
        Assert.Equal(new DateTime(2026, 9, 17, 22, 29, 44), decision.Date?.WallClock);
        Assert.Equal(TimeSpan.FromHours(2), decision.Date?.Offset);
    }

    [Fact]
    public void DeviceProfileName_WithoutMetadata_IsProbable()
    {
        var decision = Decide("24-07-11 17-07-10 0477.jpg");

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.ProfileName, decision.Reason);
    }

    [Fact]
    public void XmpAlone_IsProbable()
    {
        var xmp = new DateCandidate(new DateTime(2019, 5, 1, 10, 11, 12), DatePrecision.Second, CandidateOrigin.XmpSidecar);

        var decision = Decide("scan.jpg", dates: xmp);

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.XmpOnly, decision.Reason);
    }

    [Fact]
    public void UtcHeaderAlone_IsProbable_InThePcZone()
    {
        var decision = Decide("clip.mp4", dates: Utc(new DateTime(2024, 1, 5, 14, 0, 0), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.UtcOnly, decision.Reason);
        Assert.Equal(new DateTime(2024, 1, 5, 15, 0, 0), decision.Date?.WallClock);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.AssumedZone));
    }

    [Fact]
    public void EpochName_IsConvertedToThePcZone()
    {
        var decision = Decide("mmexport1700000000000.jpg");

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Equal(new DateTime(2023, 11, 14, 23, 13, 20), decision.Date?.WallClock);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.AssumedZone));
    }

    [Fact]
    public void GenericName_WithTime_IsProbable_DayOnly_IsUncertain()
    {
        Assert.Equal(Reasons.GenericName, Decide("vacances 2019-05-01 10-11-12.jpg").Reason);
        Assert.Equal(Confidence.Probable, Decide("vacances 2019-05-01 10-11-12.jpg").Confidence);
        Assert.Equal(Reasons.GenericNameDay, Decide("scan 20190501.jpg").Reason);
        Assert.Equal(Confidence.Uncertain, Decide("scan 20190501.jpg").Confidence);
    }

    [Fact]
    public void AmbiguousName_IsUncertain_WithoutADate()
    {
        var decision = Decide("03-04-2025 10-00-00.jpg");

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.Ambiguous, decision.Reason);
        Assert.Null(decision.Date);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.Ambiguous));
    }

    [Fact]
    public void FileDateOnly_IsUncertain_AndNeverApplicable()
    {
        var decision = Decide("random.jpg");

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.FileDateOnly, decision.Reason);
        Assert.Equal(new DateTime(2023, 3, 1, 10, 0, 0), decision.Date?.WallClock);
        Assert.False(decision.IsApplicable);
    }

    [Fact]
    public void NothingPlausible_IsNone()
    {
        var decision = Decide("random.jpg", lastWriteUtc: new DateTime(1980, 1, 1));

        Assert.Equal(Confidence.None, decision.Confidence);
        Assert.Equal(Reasons.Nothing, decision.Reason);
        Assert.Null(decision.Date);
    }

    [Fact]
    public void UnreadableFile_IsNone_EvenWithADatedName()
    {
        var decision = Arbiter.Decide(
            ProfileMatcher.Read("IMG-20210915-WA0005.jpg", null, null), new RawMetadata { ReadError = "boom" }, CopiedOn);

        Assert.Equal(Confidence.None, decision.Confidence);
        Assert.Equal(Reasons.ReadFailed, decision.Reason);
    }

    [Fact]
    public void ImplausibleCandidates_AreRejected_ButListed()
    {
        var resetClock = Exif(new DateTime(2000, 1, 1, 0, 0, 0));

        var decision = Decide("IMG_20230304_132015.jpg", dates: resetClock);

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Contains(resetClock, decision.Rejected);
        Assert.DoesNotContain(resetClock, decision.Candidates);
    }

    [Fact]
    public void CopySuffix_SetsTheDerivativeFlag()
    {
        var decision = Decide("IMG_20230304_132015-min.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.True(decision.Flags.HasFlag(DecisionFlags.Derivative));
    }

    [Fact]
    public void GameCapture_IsAScreenshot()
    {
        var decision = Decide("Wow_2024_05_06_21_10_33_123.png");

        Assert.Equal(Reasons.ScreenshotName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }
}
