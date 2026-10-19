using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Catalog;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Cli.Report;

public sealed record ProfileLine(string Profile, int Count, int Applicable);

public sealed record PatternLine(string Pattern, int Count);

/// <summary>Résumé affiché à la fin de l'analyse (spec de phase 1, §9). Les motifs inconnus désignent les profils manquants.</summary>
public sealed record AnalysisSummary(
    int Total,
    IReadOnlyDictionary<Confidence, int> ByConfidence,
    IReadOnlyList<ProfileLine> Profiles,
    IReadOnlyList<PatternLine> UnknownPatterns,
    ScanStatistics Statistics,
    int ReadErrors,
    TimeSpan Duration)
{
    public const int UnknownPatternCount = 20;

    private static readonly Regex Digit = new(@"\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int Applicable => ByConfidence.GetValueOrDefault(Confidence.Certain) + ByConfidence.GetValueOrDefault(Confidence.Probable);

    public static AnalysisSummary From(AnalysisResult analysis)
    {
        var results = analysis.Results;
        var byConfidence = Enum.GetValues<Confidence>().ToDictionary(c => c, c => results.Count(r => r.Decision.Confidence == c));
        var profiles = results
            .GroupBy(r => r.Profile?.Id ?? "inconnu")
            .Select(g => new ProfileLine(g.Key, g.Count(), g.Count(r => r.Decision.IsApplicable)))
            .OrderByDescending(line => line.Count)
            .ThenBy(line => line.Profile, StringComparer.Ordinal)
            .ToList();
        var unknown = results
            .Where(r => r.Profile is null)
            .GroupBy(r => Pattern(r.Facts.File.FileName))
            .Select(g => new PatternLine(g.Key, g.Count()))
            .OrderByDescending(line => line.Count)
            .ThenBy(line => line.Pattern, StringComparer.Ordinal)
            .Take(UnknownPatternCount)
            .ToList();

        return new AnalysisSummary(
            results.Count,
            byConfidence,
            profiles,
            unknown,
            analysis.Statistics,
            results.Count(r => r.Facts.Metadata.ReadError is not null),
            analysis.Duration);
    }

    /// <summary>Motif d'un nom : chiffres remplacés par #, extension en minuscules.</summary>
    public static string Pattern(string fileName) =>
        Digit.Replace(Path.GetFileNameWithoutExtension(fileName), "#") + Path.GetExtension(fileName).ToLowerInvariant();

    public static string Percent(int part, int total) =>
        (total == 0 ? 0 : 100.0 * part / total).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    public void Print(TextWriter writer)
    {
        writer.WriteLine($"Analyse terminée : {Total} médias en {FormatDuration(Duration)}.");
        writer.WriteLine();
        writer.WriteLine("Confiance");
        foreach (var confidence in new[] { Confidence.Certain, Confidence.Probable, Confidence.Uncertain, Confidence.None })
        {
            var count = ByConfidence.GetValueOrDefault(confidence);
            writer.WriteLine($"  {DecisionTexts.ConfidenceLabel(confidence),-12}{count,8}  {Percent(count, Total),8}");
        }

        writer.WriteLine($"  Sûre + Probable : {Percent(Applicable, Total)}");
        writer.WriteLine();
        writer.WriteLine("Par profil (nombre, % Sûre + Probable)");
        foreach (var line in Profiles)
        {
            writer.WriteLine($"  {line.Profile,-28}{line.Count,8}  {Percent(line.Applicable, line.Count),8}");
        }

        if (UnknownPatterns.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Motifs de nom inconnus les plus fréquents");
            foreach (var line in UnknownPatterns)
            {
                writer.WriteLine($"  {line.Pattern,-40}{line.Count,8}");
            }
        }

        writer.WriteLine();
        writer.WriteLine(
            $"Exclus : {Statistics.HiddenFiles} fichier(s) caché(s), {Statistics.HiddenFolders} dossier(s) caché(s), " +
            $"{Statistics.EmptyFiles} fichier(s) vide(s), {Statistics.InaccessibleFolders} dossier(s) inaccessible(s).");
        writer.WriteLine($"Fichiers illisibles : {ReadErrors}.");
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes} min {duration.Seconds} s"
        : duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
