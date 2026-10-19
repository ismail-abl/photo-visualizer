using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class ImageFileInfoTests
{
    private static ImageFileInfo With(Confidence confidence, string reason, DateCandidate? date) =>
        new("/p/x.png", "x.png", ".png", 10, new DateDecision(date, confidence, reason, [], []));

    private static readonly DateCandidate WhatsAppDay = new(new DateTime(2021, 9, 15), DatePrecision.Day, CandidateOrigin.FileName);

    [Fact]
    public void Applicable_ShowsConfidenceReasonAndPreview()
    {
        var image = With(Confidence.Certain, Reasons.MessagingName, WhatsAppDay);

        Assert.Equal("2021-09-15", image.RetainedDateText);
        Assert.Equal("Sûre — Nom donné par la messagerie (fait foi)", image.DecisionText);
        Assert.Equal("IMG-20210915-000000-01.jpg", image.NormalizedFileNamePreview);
        Assert.Equal(string.Empty, image.StatusText);
    }

    [Fact]
    public void Uncertain_IsToReview_WithoutPreview()
    {
        var fileDate = new DateCandidate(new DateTime(2023, 3, 1, 10, 0, 0), DatePrecision.Second, CandidateOrigin.FileModified);
        var image = With(Confidence.Uncertain, Reasons.FileDateOnly, fileDate);

        Assert.Null(image.NormalizedFileNamePreview);
        Assert.Equal("À vérifier : Seulement la date du fichier (souvent celle de la copie)", image.StatusText);
    }

    [Fact]
    public void None_SaysWhy()
    {
        var image = With(Confidence.None, Reasons.ReadFailed, null);

        Assert.Equal("—", image.RetainedDateText);
        Assert.Equal("Aucune date fiable : Fichier illisible", image.StatusText);
    }
}
