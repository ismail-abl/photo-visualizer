using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class DecisionTextsTests
{
    [Theory]
    [InlineData(Confidence.Certain, "Sûre")]
    [InlineData(Confidence.Probable, "Probable")]
    [InlineData(Confidence.Uncertain, "Incertaine")]
    [InlineData(Confidence.None, "Aucune")]
    public void ConfidenceLabel_IsFrench(Confidence confidence, string expected)
    {
        Assert.Equal(expected, DecisionTexts.ConfidenceLabel(confidence));
    }

    [Fact]
    public void ReasonLabel_ExistsForEveryReason_AndFallsBackToTheId()
    {
        var reasons = typeof(Reasons).GetFields().Select(f => (string)f.GetValue(null)!);

        Assert.All(reasons, reason => Assert.NotEqual(reason, DecisionTexts.ReasonLabel(reason)));
        Assert.Equal("inconnue", DecisionTexts.ReasonLabel("inconnue"));
    }

    [Fact]
    public void FormatCandidate_ShowsPrecisionOffsetAndUtc()
    {
        Assert.Equal("2024-01-05", DecisionTexts.FormatCandidate(new DateCandidate(new DateTime(2024, 1, 5), DatePrecision.Day)));
        Assert.Equal("2024-01-05 15:04:12+09:00", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, offset: TimeSpan.FromHours(9))));
        Assert.Equal("2024-01-05 15:04:12-03:30", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, offset: TimeSpan.FromHours(-3.5))));
        Assert.Equal("2024-01-05 14:00:00 UTC", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, isUtc: true)));
        Assert.Equal("2024-01-05 15:04:12.123", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12, 123), DatePrecision.SubSecond)));
    }

    [Fact]
    public void FlagLabels_ListEverySetFlag()
    {
        Assert.Equal(["ambigue", "live-photo"], DecisionTexts.FlagLabels(DecisionFlags.Ambiguous | DecisionFlags.LivePhoto));
        Assert.Equal(["source-ignoree", "live-photo-conflit"], DecisionTexts.FlagLabels(DecisionFlags.SourceIgnored | DecisionFlags.LivePhotoConflict));
        Assert.Empty(DecisionTexts.FlagLabels(DecisionFlags.None));
    }
}
