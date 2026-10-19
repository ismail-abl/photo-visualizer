using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <summary>
/// Choisit la date d'un média et sa confiance par des règles nommées (spec de phase 1, §5.4) :
/// étape 1, le candidat principal ; étape 2, sa confrontation aux autres candidats forts.
/// </summary>
public sealed class DateArbiter
{
    private readonly NormalizationOptions _options;

    public DateArbiter(NormalizationOptions options)
    {
        _options = options;
    }

    public DateDecision Decide(NameReading name, RawMetadata metadata, DateTime lastWriteUtc)
    {
        var flags = name.IsDerivative ? DecisionFlags.Derivative : DecisionFlags.None;
        if (name.Ambiguous is not null)
        {
            flags |= DecisionFlags.Ambiguous;
        }

        if (metadata.ReadError is not null)
        {
            return new DateDecision(null, Confidence.None, Reasons.ReadFailed, [], [], flags);
        }

        var all = new List<DateCandidate>(metadata.Dates);
        if (name.Date is not null)
        {
            all.Add(name.Date);
        }

        all.Add(new DateCandidate(lastWriteUtc, DatePrecision.Second, CandidateOrigin.FileModified, isUtc: true));

        var plausible = all.Where(c => DatePlausibility.IsPlausible(c, _options.UtcNow)).ToList();
        var rejected = all.Where(c => !DatePlausibility.IsPlausible(c, _options.UtcNow)).ToList();
        var candidates = new Candidates(plausible, name);

        var choice = Confront(SelectPrimary(candidates), candidates);

        var date = choice.Date;
        if (date is { IsUtc: true })
        {
            date = date.Localize(_options.LocalZone);
            flags |= DecisionFlags.AssumedZone;
        }

        return new DateDecision(date, choice.Confidence, choice.Reason, plausible, rejected, flags | choice.Flags);
    }

    // Étape 1 : la première règle qui s'applique décide.
    private static Choice SelectPrimary(Candidates c)
    {
        var family = c.Reading.Profile?.Family;
        var exif = c.Exif;
        var profileName = c.ProfileName;

        if (family == ProfileFamily.Normalized && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.AlreadyNormalized);
        }

        if (family == ProfileFamily.Messaging && exif is not null && profileName is not null
            && DateComparison.CompareAt(exif, profileName, profileName.Precision) <= 0)
        {
            return FromExif(exif, c, Reasons.MessagingExif);
        }

        if (family == ProfileFamily.Messaging && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.MessagingName);
        }

        if (family == ProfileFamily.Screenshot && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.ScreenshotName);
        }

        if (exif is { Offset: not null })
        {
            return new Choice(exif, Confidence.Certain, Reasons.ExifWithOffset);
        }

        if (c.Apple is not null)
        {
            return new Choice(c.Apple, Confidence.Certain, Reasons.QuickTimeApple);
        }

        if (exif is not null)
        {
            return FromExif(exif, c, reason: null);
        }

        if (c.MovieHeader is not null && profileName is not null && DateComparison.ZoneGap(profileName, c.MovieHeader) is { } gap)
        {
            return new Choice(profileName with { Offset = gap }, Confidence.Certain, Reasons.VideoUtcName);
        }

        if (profileName is not null)
        {
            return new Choice(profileName, Confidence.Probable, Reasons.ProfileName);
        }

        if (c.Xmp is not null)
        {
            return new Choice(c.Xmp, Confidence.Probable, Reasons.XmpOnly);
        }

        if (c.MovieHeader is not null)
        {
            return new Choice(c.MovieHeader, Confidence.Probable, Reasons.UtcOnly);
        }

        if (c.Name is not null)
        {
            return c.Name.HasTime
                ? new Choice(c.Name, Confidence.Probable, Reasons.GenericName)
                : new Choice(c.Name, Confidence.Uncertain, Reasons.GenericNameDay);
        }

        if (c.Reading.Ambiguous is not null)
        {
            return new Choice(null, Confidence.Uncertain, Reasons.Ambiguous, DecisionFlags.Ambiguous);
        }

        if (c.FileModified is not null)
        {
            return new Choice(c.FileModified, Confidence.Uncertain, Reasons.FileDateOnly);
        }

        return new Choice(null, Confidence.None, Reasons.Nothing);
    }

    // Étape 2 : le candidat principal face à chaque autre candidat fort. Un désaccord inexpliqué fait tomber la confiance.
    private static Choice Confront(Choice choice, Candidates c)
    {
        if (choice.Date is null)
        {
            return choice;
        }

        foreach (var other in c.Strong)
        {
            if (other.Origin == choice.Date.Origin || Agrees(choice.Date, other))
            {
                continue;
            }

            return choice with
            {
                Confidence = Confidence.Uncertain,
                Reason = Reasons.Disagreement,
                Flags = choice.Flags | DecisionFlags.Disagreement
            };
        }

        return choice;
    }

    private static bool Agrees(DateCandidate primary, DateCandidate other)
    {
        if (DateComparison.Concordant(primary, other))
        {
            return true;
        }

        // Une heure locale et une valeur UTC séparées par un fuseau valide.
        if ((!primary.IsUtc && other.IsUtc && DateComparison.ZoneGap(primary, other) is not null)
            || (primary.IsUtc && !other.IsUtc && DateComparison.ZoneGap(other, primary) is not null))
        {
            return true;
        }

        // Latence d'écriture : l'EXIF d'un appareil est écrit quelques secondes après l'heure du nom.
        if (IsName(primary) && IsExif(other) && DateComparison.ExifMatchesName(other, primary))
        {
            return true;
        }

        if (IsName(other) && IsExif(primary) && DateComparison.ExifMatchesName(primary, other))
        {
            return true;
        }

        // Enregistrement ou transfert postérieur : un nom plus récent que l'EXIF est l'heure d'enregistrement d'une copie
        // retouchée ou exportée, ou de réception d'un transfert (messagerie, téléchargement), quel que soit le profil.
        return other.Origin == CandidateOrigin.FileName
               && primary.Origin is CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized
               && DateComparison.CompareAt(primary, other, other.Precision) <= 0;
    }

    private static bool IsName(DateCandidate candidate) => candidate is { Origin: CandidateOrigin.FileName, IsUtc: false };

    private static bool IsExif(DateCandidate candidate) => candidate.Origin is CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized;

    // EXIF sans décalage : Sûr s'il concorde avec le nom d'un profil ou si le GPS en déduit le décalage, Probable sinon.
    private static Choice FromExif(DateCandidate exif, Candidates c, string? reason)
    {
        if (exif.Offset is not null)
        {
            return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifWithOffset);
        }

        if (c.ProfileName is { } name)
        {
            if (!name.IsUtc && DateComparison.ExifMatchesName(exif, name))
            {
                // Le nom confirme l'heure ; le GPS, s'il existe, fournit en plus le décalage (spec §5.4).
                if (c.Gps is not null && DateComparison.OffsetFromGps(exif, c.Gps) is { } confirmedOffset)
                {
                    return new Choice(exif with { Offset = confirmedOffset }, Confidence.Certain, reason ?? Reasons.ExifConcordant);
                }

                return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifConcordant);
            }

            if (name.IsUtc && DateComparison.ZoneGap(exif, name) is { } nameGap)
            {
                return new Choice(exif with { Offset = nameGap }, Confidence.Certain, reason ?? Reasons.ExifConcordant);
            }
        }

        if (c.Gps is not null && DateComparison.OffsetFromGps(exif, c.Gps) is { } gpsOffset)
        {
            return new Choice(exif with { Offset = gpsOffset }, Confidence.Certain, reason ?? Reasons.ExifConcordant);
        }

        // Décision de l'utilisateur : un fichier édité (Xiaomi...) garde l'EXIF d'origine et prend un nom à l'heure de l'édition ;
        // quand l'EXIF précède le nom d'un profil d'appareil, l'EXIF est la source de vérité.
        if (c.ProfileName is { IsUtc: false } deviceName && c.Reading.Profile?.Family == ProfileFamily.Camera
            && DateComparison.CompareAt(exif, deviceName, deviceName.Precision) < 0)
        {
            return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifBeforeName);
        }

        return new Choice(exif, Confidence.Probable, reason ?? Reasons.ExifOnly);
    }

    private sealed record Choice(DateCandidate? Date, Confidence Confidence, string Reason, DecisionFlags Flags = DecisionFlags.None);

    private sealed record Candidates(IReadOnlyList<DateCandidate> Plausible, NameReading Reading)
    {
        public DateCandidate? Exif => Get(CandidateOrigin.ExifOriginal) ?? Get(CandidateOrigin.ExifDigitized);

        public DateCandidate? Apple => Get(CandidateOrigin.QuickTimeApple);

        public DateCandidate? MovieHeader => Get(CandidateOrigin.QuickTimeMovieHeader);

        public DateCandidate? Xmp => Get(CandidateOrigin.Xmp) ?? Get(CandidateOrigin.XmpSidecar);

        public DateCandidate? Gps => Get(CandidateOrigin.Gps);

        public DateCandidate? Name => Get(CandidateOrigin.FileName);

        /// <summary>Le nom n'est un candidat fort que lu par un motif de profil.</summary>
        public DateCandidate? ProfileName => Reading.FromProfilePattern ? Name : null;

        public DateCandidate? FileModified => Get(CandidateOrigin.FileModified);

        /// <summary>Candidats forts : métadonnées internes, et le nom s'il est lu par un profil. Ni GPS ni date du fichier.</summary>
        public IEnumerable<DateCandidate> Strong => Plausible.Where(candidate => candidate.Origin switch
        {
            CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized or CandidateOrigin.QuickTimeApple
                or CandidateOrigin.QuickTimeMovieHeader or CandidateOrigin.Xmp or CandidateOrigin.XmpSidecar => true,
            CandidateOrigin.FileName => Reading.FromProfilePattern,
            _ => false
        });

        public DateCandidate? Get(CandidateOrigin origin) => Plausible.FirstOrDefault(c => c.Origin == origin);
    }
}
