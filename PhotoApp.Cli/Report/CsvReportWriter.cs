using System.Text;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Cli.Report;

/// <summary>Rapport CSV de l'analyse (spec de phase 1, §9) : UTF-8 avec BOM, séparateur « ; », une ligne par média.</summary>
public static class CsvReportWriter
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "chemin", "type", "format_reel", "profil", "date", "precision", "decalage", "confiance", "raison", "nom_propose",
        "exif", "quicktime", "gps", "xmp", "nom", "date_fichier", "fichiers_lies", "drapeaux", "erreur"
    ];

    public static void WriteFile(string path, IEnumerable<MediaResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Write(writer, results);
    }

    public static void Write(TextWriter writer, IEnumerable<MediaResult> results)
    {
        writer.WriteLine(string.Join(';', Columns));
        foreach (var result in results)
        {
            writer.WriteLine(string.Join(';', Row(result).Select(Escape)));
        }
    }

    private static IEnumerable<string> Row(MediaResult result)
    {
        var decision = result.Decision;
        var date = decision.Date;
        var linked = result.Facts.File.Sidecars.Select(path => Path.GetFileName(path)).ToList();
        if (result.LivePhotoPartner is not null)
        {
            linked.Add(result.LivePhotoPartner);
        }

        return
        [
            result.Facts.File.FullPath,
            result.Kind == MediaKind.Video ? "video" : "photo",
            result.TargetExtension.TrimStart('.'),
            result.Profile?.Id ?? "inconnu",
            date is null ? string.Empty : DecisionTexts.FormatDate(date),
            date is null ? string.Empty : DecisionTexts.PrecisionLabel(date.Precision),
            date is null ? string.Empty : DecisionTexts.FormatOffset(date.Offset),
            DecisionTexts.ConfidenceLabel(decision.Confidence),
            decision.Reason,
            result.ProposedName ?? string.Empty,
            Candidates(decision, CandidateOrigin.ExifOriginal, CandidateOrigin.ExifDigitized),
            Candidates(decision, CandidateOrigin.QuickTimeApple, CandidateOrigin.QuickTimeMovieHeader),
            Candidates(decision, CandidateOrigin.Gps),
            Candidates(decision, CandidateOrigin.Xmp, CandidateOrigin.XmpSidecar),
            Candidates(decision, CandidateOrigin.FileName),
            Candidates(decision, CandidateOrigin.FileModified),
            string.Join('|', linked),
            string.Join('|', DecisionTexts.FlagLabels(decision.Flags)),
            result.Facts.Metadata.ReadError ?? string.Empty
        ];
    }

    // Candidats des origines demandées ; les valeurs écartées (implausibles) restent visibles, marquées.
    private static string Candidates(DateDecision decision, params CandidateOrigin[] origins) =>
        string.Join(" | ", decision.Candidates
            .Where(candidate => origins.Contains(candidate.Origin))
            .Select(DecisionTexts.FormatCandidate)
            .Concat(decision.Rejected
                .Where(candidate => origins.Contains(candidate.Origin))
                .Select(candidate => DecisionTexts.FormatCandidate(candidate) + " (écartée)")));

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
