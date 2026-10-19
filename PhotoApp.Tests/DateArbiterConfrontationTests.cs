using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using static PhotoApp.Tests.DateArbiterTests;

namespace PhotoApp.Tests;

public sealed class DateArbiterConfrontationTests
{
    [Fact]
    public void ExifAgainstAnotherDayInXmp_IsAnUnexplainedDisagreement()
    {
        var xmp = new DateCandidate(new DateTime(2022, 6, 1, 10, 0, 0), DatePrecision.Second, CandidateOrigin.Xmp);

        var decision = Decide("photo.jpg", dates: [Exif(new DateTime(2024, 1, 5, 15, 4, 12), TimeSpan.FromHours(9)), xmp]);

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.Disagreement));
        Assert.Equal(new DateTime(2024, 1, 5, 15, 4, 12), decision.Date?.WallClock);
        Assert.False(decision.IsApplicable);
    }

    [Fact]
    public void WhatsApp_WithAnExifLaterThanTheName_IsADisagreement()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2022, 1, 1, 10, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.Equal(Confidence.Uncertain, decision.Confidence);
    }

    [Fact]
    public void Download_WithAnOlderExif_IsAnExplainedTransfer()
    {
        var decision = Decide("mmexport1700000000000.jpg", dates: Exif(new DateTime(2020, 1, 1, 10, 0, 0)));

        Assert.Equal(Reasons.ExifOnly, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.False(decision.Flags.HasFlag(DecisionFlags.Disagreement));
    }

    [Fact]
    public void DeviceName_EarlierThanTheExifBeyondTheLatency_IsADisagreement()
    {
        var decision = Decide("IMG_20230304_132015.jpg", dates: Exif(new DateTime(2023, 3, 4, 14, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
    }

    [Fact]
    public void DeviceName_LaterThanTheExif_IsASaveAfterTheShot()
    {
        var decision = Decide("IMG_20250704_160803.jpg", "Xiaomi", dates: Exif(new DateTime(2025, 6, 20, 21, 48, 56)));

        Assert.Equal(Reasons.ExifBeforeName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }

    [Fact]
    public void ExifWithOffset_ContradictingTheVideoHeader_IsADisagreement()
    {
        var decision = Decide("photo.mov", dates: [Exif(new DateTime(2024, 7, 11, 17, 7, 10), TimeSpan.FromHours(2)), Utc(new DateTime(2024, 7, 11, 12, 7, 10), CandidateOrigin.QuickTimeMovieHeader)]);

        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.Equal(Confidence.Uncertain, decision.Confidence);
    }

    [Fact]
    public void AgreeingSources_StayCertain()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var movieHeader = Utc(new DateTime(2024, 7, 11, 15, 7, 11), CandidateOrigin.QuickTimeMovieHeader);
        var samsung = Exif(new DateTime(2026, 9, 17, 22, 29, 44), TimeSpan.FromHours(2));

        var video = Decide("24-07-11 17-07-10 0477.mov", dates: [apple, movieHeader]);
        var photo = Decide("20260917_222944.heic", "samsung", dates: samsung);

        Assert.Equal(Confidence.Certain, video.Confidence);
        Assert.Equal(Reasons.QuickTimeApple, video.Reason);
        Assert.Equal(Confidence.Certain, photo.Confidence);
        Assert.Equal(Reasons.ExifWithOffset, photo.Reason);
    }

    [Fact]
    public void GenericName_IsNotStrong_AndCannotDisagree()
    {
        var decision = Decide("vacances 2019-05-01 10-11-12.jpg", dates: Exif(new DateTime(2018, 7, 1, 10, 0, 0)));

        Assert.Equal(Reasons.ExifOnly, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
    }

    [Fact]
    public void FileDate_NeverDisagrees()
    {
        var decision = Decide("IMG_20230304_132015.jpg", lastWriteUtc: new DateTime(2025, 12, 24, 10, 0, 0));

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
    }

    [Fact]
    public void SamsungExif_WrittenSecondsAfterTheName_StaysCertain()
    {
        var decision = Decide("20261012_014932.jpg", "samsung", dates: Exif(new DateTime(2026, 10, 12, 1, 49, 34, 625), TimeSpan.FromHours(2)));

        Assert.Equal(Reasons.ExifWithOffset, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }

    [Fact]
    public void XiaomiExifWithoutOffset_WrittenSecondsAfterTheName_IsConcordant()
    {
        var decision = Decide("IMG_20230304_132015.jpg", "Xiaomi", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 19)));

        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }

    [Fact]
    public void AndroidVideo_HeaderWrittenAtTheEnd_GivesTheNameAndTheOffset()
    {
        var decision = Decide("VID_20231027_195214.mp4", dates: Utc(new DateTime(2023, 10, 27, 17, 52, 46), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Reasons.VideoUtcName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(new DateTime(2023, 10, 27, 19, 52, 14), decision.Date?.WallClock);
        Assert.Equal(TimeSpan.FromHours(2), decision.Date?.Offset);
    }

    [Fact]
    public void IPhoneVideo_AppleDateAndLaterHeader_StaysCertain()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 8, 14, 4, 59), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));

        var decision = Decide("24-07-08 14-05-00 0461.mov", dates: [apple, Utc(new DateTime(2024, 7, 8, 12, 5, 18), CandidateOrigin.QuickTimeMovieHeader)]);

        Assert.Equal(Reasons.QuickTimeApple, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }

    [Fact]
    public void WhatsAppVideo_DayNameAndUtcHeader_StaysCertain()
    {
        var decision = Decide("VID-20230901-WA0008.mp4", dates: Utc(new DateTime(2023, 9, 1, 21, 34, 16), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Reasons.MessagingName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
    }
}
