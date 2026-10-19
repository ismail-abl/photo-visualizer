using PhotoApp.Core.Normalization;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Models;

/// <summary>Une photo de l'interface, avec la décision du moteur. Seules Sûre et Probable sont appliquées.</summary>
public sealed class ImageFileInfo
{
    public ImageFileInfo(
        string fullPath,
        string fileName,
        string extension,
        long fileSizeBytes,
        DateDecision? decision = null)
    {
        FullPath = fullPath;
        FileName = fileName;
        Extension = extension;
        FileSizeBytes = fileSizeBytes;
        Decision = decision;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string Extension { get; }

    public long FileSizeBytes { get; }

    public DateDecision? Decision { get; }

    public string RetainedDateText => Decision?.Date is { } date ? DecisionTexts.FormatDate(date) : "—";

    public string DecisionText => Decision is null
        ? DecisionTexts.ConfidenceLabel(Confidence.None)
        : $"{DecisionTexts.ConfidenceLabel(Decision.Confidence)} — {DecisionTexts.ReasonLabel(Decision.Reason)}";

    public string? NormalizedFileNamePreview =>
        Decision is { IsApplicable: true, Date: { } date } ? NormalizedFileNameBuilder.Preview(date) : null;

    public string StatusText => Decision switch
    {
        null or { IsApplicable: true } => string.Empty,
        { Confidence: Confidence.Uncertain } => "À vérifier : " + DecisionTexts.ReasonLabel(Decision.Reason),
        _ => "Aucune date fiable : " + DecisionTexts.ReasonLabel(Decision.Reason)
    };

    public static ImageFileInfo FromResult(MediaResult result) => new(
        result.Facts.File.FullPath,
        result.Facts.File.FileName,
        result.Facts.File.Extension,
        result.Facts.File.SizeBytes,
        result.Decision);
}
