using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Core.Normalization;

/// <param name="TargetExtension">Extension normalisée du vrai format, avec le point.</param>
/// <param name="ProposedName">Nom proposé (Sûre ou Probable), sinon null. Rien n'est renommé en phase 1.</param>
public sealed record MediaResult(
    MediaFacts Facts,
    MediaKind Kind,
    string TargetExtension,
    NameReading Reading,
    DateDecision Decision,
    string? ProposedName,
    string? LivePhotoPartner)
{
    public SourceProfile? Profile => Reading.Profile;
}
