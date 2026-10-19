using System.Diagnostics;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Metadata;

namespace PhotoApp.Catalog;

public readonly record struct AnalysisProgress(int Done, int Total);

public sealed record AnalysisResult(IReadOnlyList<MediaResult> Results, ScanStatistics Statistics, TimeSpan Duration);

/// <summary>
/// L'analyse complète, sans aucune écriture : scan des dossiers, lecture des métadonnées en parallèle, puis moteur.
/// Partagée par la CLI et l'interface.
/// </summary>
public sealed class MediaAnalyzer
{
    private readonly MediaScanner _scanner = new();
    private readonly MetadataReader _reader = new();

    public AnalysisResult Analyze(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> excludedFolders,
        IProgress<AnalysisProgress>? progress = null,
        NormalizationOptions? options = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var media = new List<ScannedMedia>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var statistics = ScanStatistics.None;

        foreach (var root in roots)
        {
            var scan = _scanner.Scan(root, new ScanOptions(excludedFolders));
            statistics = statistics.Add(scan.Statistics);
            media.AddRange(scan.Media.Where(item => seen.Add(item.FullPath)));
        }

        var facts = new MediaFacts[media.Count];
        var done = 0;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.For(0, media.Count, parallel, index =>
        {
            RawMetadata raw;
            try
            {
                raw = _reader.Read(media[index].FullPath, media[index].Sidecars);
            }
            catch (Exception ex)
            {
                // Seconde garde : un fichier qui fait lever la lecture ne doit jamais arrêter l'analyse des autres.
                raw = new RawMetadata { ReadError = ex.Message };
            }

            facts[index] = new MediaFacts(media[index], raw);
            progress?.Report(new AnalysisProgress(Interlocked.Increment(ref done), media.Count));
        });

        var engine = new NormalizationEngine(options ?? NormalizationOptions.ForNow(), new Sha256ContentHasher());
        return new AnalysisResult(engine.Normalize(facts), statistics, stopwatch.Elapsed);
    }
}
