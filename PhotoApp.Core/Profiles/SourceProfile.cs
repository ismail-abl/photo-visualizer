using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// Profil de source, déclaratif (spec de phase 1, §6). Les motifs s'appliquent au nom sans extension et utilisent
/// les groupes nommés year, month, day, hour, minute, second, fraction, ampm, epoch, d1/d2 (jour ou mois) et counter.
/// </summary>
/// <param name="MakePattern">Marque EXIF ou QuickTime : reconnaissance sans motif de nom, ou condition du motif.</param>
/// <param name="NameNeedsMake">Le motif est trop courant pour suffire seul (IMG_1234, IMG_AAAAMMJJ_HHmmss) : la marque doit concorder.</param>
public sealed record SourceProfile(
    string Id,
    string DisplayName,
    ProfileFamily Family,
    IReadOnlyList<Regex> NamePatterns,
    NameTimeSemantics TimeSemantics = NameTimeSemantics.Local,
    DayMonthOrder DayMonthOrder = DayMonthOrder.Unknown,
    Regex? MakePattern = null,
    bool NameNeedsMake = false,
    Regex? FolderPattern = null);
