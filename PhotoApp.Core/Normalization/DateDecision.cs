using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Date retenue, confiance et raison (identifiant de la règle), avec tous les candidats pour le rapport.</summary>
public sealed record DateDecision(
    DateCandidate? Date,
    Confidence Confidence,
    string Reason,
    IReadOnlyList<DateCandidate> Candidates,
    IReadOnlyList<DateCandidate> Rejected,
    DecisionFlags Flags = DecisionFlags.None)
{
    public bool IsApplicable => Date is not null && Confidence >= Confidence.Probable;
}
