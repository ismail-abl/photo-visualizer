using PhotoApp.Cli.Report;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class AnalysisSummaryTests
{
    private static AnalysisSummary Summarize(params string[] paths) =>
        AnalysisSummary.From(new AnalysisResult(
            NormalizationEngineTests.Engine().Normalize(paths.Select(path => NormalizationEngineTests.Facts(path)).ToList()),
            new ScanStatistics(1, 2, 3, 4),
            TimeSpan.FromSeconds(75)));

    [Fact]
    public void From_CountsConfidencesProfilesAndUnknownPatterns()
    {
        var summary = Summarize("/p/IMG-20210915-WA0005.jpg", "/p/IMG_20230304_132015.jpg", "/p/random.jpg", "/p/hqdefault-4B744.jpg");

        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.ByConfidence[Confidence.Certain]);
        Assert.Equal(1, summary.ByConfidence[Confidence.Probable]);
        Assert.Equal(2, summary.ByConfidence[Confidence.Uncertain]);
        Assert.Equal(2, summary.Applicable);
        Assert.Contains(new ProfileLine("whatsapp", 1, 1), summary.Profiles);
        Assert.Contains(new ProfileLine("inconnu", 1, 0), summary.Profiles);
        Assert.Equal([new PatternLine("random.jpg", 1)], summary.UnknownPatterns);
    }

    [Fact]
    public void UnknownPatterns_AreLimitedToTwenty()
    {
        var summary = Summarize(Enumerable.Range(0, 25).Select(i => $"/p/{(char)('a' + i)}.jpg").ToArray());

        Assert.Equal(AnalysisSummary.UnknownPatternCount, summary.UnknownPatterns.Count);
    }

    [Theory]
    [InlineData("hqdefault-4B744.jpg", "hqdefault-#B###.jpg")]
    [InlineData("IMG_1234.JPG", "IMG_####.jpg")]
    public void Pattern_ReplacesDigits_AndLowercasesTheExtension(string fileName, string expected)
    {
        Assert.Equal(expected, AnalysisSummary.Pattern(fileName));
    }

    [Fact]
    public void Print_ShowsTheKeyFigures()
    {
        var writer = new StringWriter();

        Summarize("/p/IMG-20210915-WA0005.jpg", "/p/random.jpg").Print(writer);

        var text = writer.ToString();
        Assert.Contains("Analyse terminée : 2 médias en 1 min 15 s.", text);
        Assert.Contains("Sûre + Probable : 50.0 %", text);
        Assert.Contains("Motifs de nom inconnus les plus fréquents", text);
        Assert.Contains("1 fichier(s) caché(s), 2 dossier(s) caché(s), 3 fichier(s) vide(s), 4 dossier(s) inaccessible(s)", text);
    }

    [Fact]
    public void Percent_OfNothing_IsZero()
    {
        Assert.Equal("0.0 %", AnalysisSummary.Percent(0, 0));
    }
}
