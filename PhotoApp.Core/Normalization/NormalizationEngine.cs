using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <summary>
/// Point d'entrée du moteur (spec de phase 1, §4) : profils et noms, ambiguïtés du lot, décisions, vrai format, noms proposés.
/// Pur et déterministe : un même lot donne toujours le même résultat, quel que soit l'ordre d'entrée.
/// </summary>
public sealed class NormalizationEngine
{
    private readonly NormalizationOptions _options;
    private readonly DateArbiter _arbiter;
    private readonly MediaNamer _namer;

    public NormalizationEngine(NormalizationOptions options, IContentHasher hasher)
    {
        _options = options;
        _arbiter = new DateArbiter(options);
        _namer = new MediaNamer(hasher);
    }

    public IReadOnlyList<MediaResult> Normalize(IReadOnlyList<MediaFacts> batch)
    {
        var facts = batch.OrderBy(f => f.File.FullPath, StringComparer.Ordinal).ToList();

        var readings = AmbiguityResolver.Resolve(facts
            .Select(f => new AmbiguityInput(
                FolderOf(f.File.FullPath),
                ProfileMatcher.Read(f.File.FileName, f.Metadata.Make, Path.GetFileName(FolderOf(f.File.FullPath))),
                LocalDates(f)))
            .ToList());

        var targets = facts.Select(TargetExtension).ToList();
        var kinds = targets.Select(extension => SupportedMediaExtensions.IsVideo(extension) ? MediaKind.Video : MediaKind.Photo).ToList();
        var decisions = facts
            .Select((f, i) =>
            {
                var decision = _arbiter.Decide(readings[i], f.Metadata, f.File.LastWriteUtc);
                return SupportedMediaExtensions.SameFormat(targets[i], f.File.Extension)
                    ? decision
                    : decision with { Flags = decision.Flags | DecisionFlags.RealFormat };
            })
            .ToList();

        var names = _namer.Name(facts
            .Select((f, i) => new NamingInput(f.File.FullPath, kinds[i], targets[i], decisions[i], readings[i].Counter, f.Metadata.ContentIdentifier))
            .ToList());

        return facts
            .Select((f, i) =>
            {
                var decision = names[i].LivePhotoPartner is null
                    ? decisions[i]
                    : decisions[i] with { Flags = decisions[i].Flags | DecisionFlags.LivePhoto };
                if (f.Metadata.SourceWarnings.Count > 0)
                {
                    decision = decision with { Flags = decision.Flags | DecisionFlags.SourceIgnored };
                }

                return new MediaResult(f, kinds[i], targets[i], readings[i], decision, names[i].ProposedName, names[i].LivePhotoPartner);
            })
            .ToList();
    }

    // Autres candidats du fichier en heure murale locale, pour trancher une date JJ/MM (§5.5, étape 4).
    private IReadOnlyList<DateCandidate> LocalDates(MediaFacts facts) =>
        facts.Metadata.Dates
            .Append(new DateCandidate(facts.File.LastWriteUtc, DatePrecision.Second, CandidateOrigin.FileModified, isUtc: true))
            .Select(date => date.Localize(_options.LocalZone))
            .ToList();

    // L'extension du vrai format, sauf si l'extension actuelle désigne déjà ce format (.heif / heic, .tif / tiff…).
    private static string TargetExtension(MediaFacts facts)
    {
        var own = NormalizedFileNameBuilder.NormalizeExtension(facts.File.Extension);
        if (facts.Metadata.DetectedExtension is not { Length: > 0 } detected)
        {
            return own;
        }

        var real = NormalizedFileNameBuilder.NormalizeExtension(detected);
        return SupportedMediaExtensions.SameFormat(own, real) ? own : real;
    }

    private static string FolderOf(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
