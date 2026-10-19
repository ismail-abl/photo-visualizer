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

        var choice = SelectPrimary(candidates);

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

    // EXIF sans décalage : Sûr s'il concorde avec le nom d'un profil ou si le GPS en déduit le décalage, Probable sinon.
    private static Choice FromExif(DateCandidate exif, Candidates c, string? reason)
    {
        if (exif.Offset is not null)
        {
            return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifWithOffset);
        }

        if (c.ProfileName is { } name)
        {
            if (!name.IsUtc && DateComparison.Concordant(exif, name))
            {
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

        public DateCandidate? Get(CandidateOrigin origin) => Plausible.FirstOrDefault(c => c.Origin == origin);
    }
}
