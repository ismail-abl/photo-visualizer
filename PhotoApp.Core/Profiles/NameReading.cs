using PhotoApp.Core.Models;

namespace PhotoApp.Core.Profiles;

/// <summary>Ce que le nom d'un fichier apprend : profil reconnu, date, ambiguïté JJ/MM, compteur (WAnnnn…).</summary>
/// <param name="FromProfilePattern">La lecture vient d'un motif de profil (et non du parseur générique) : le nom est alors un candidat fort.</param>
/// <param name="IsDerivative">Un suffixe de copie (-min, (1), - Copie) a été retiré pour lire le nom.</param>
/// <param name="Evidence">Ordre JJ/MM prouvé par ce nom (une composante > 12) : sert au consensus des voisins.</param>
public sealed record NameReading(
    SourceProfile? Profile,
    DateCandidate? Date,
    AmbiguousName? Ambiguous,
    long? Counter,
    bool FromProfilePattern,
    bool IsDerivative,
    DayMonthEvidence? Evidence = null)
{
    public static NameReading None { get; } = new(null, null, null, null, false, false);
}

/// <summary>Les deux lectures d'une date JJ/MM ou MM/JJ, et la clé du motif qui l'a produite.</summary>
public sealed record AmbiguousName(DateCandidate DayFirst, DateCandidate MonthFirst, string PatternKey);

public sealed record DayMonthEvidence(string PatternKey, DayMonthOrder Order);
