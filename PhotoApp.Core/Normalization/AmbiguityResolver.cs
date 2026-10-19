using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Core.Normalization;

/// <param name="Folder">Dossier du fichier : le consensus ne vaut qu'entre voisins.</param>
/// <param name="OtherDates">Autres candidats du fichier (métadonnées, date du fichier), en heure murale locale.</param>
public sealed record AmbiguityInput(string Folder, NameReading Reading, IReadOnlyList<DateCandidate> OtherDates);

/// <summary>
/// Tranche les dates JJ/MM ambiguës d'un lot (spec de phase 1, §5.5) : étape 3, consensus des voisins lus par le même motif ;
/// étape 4, accord avec un autre candidat du fichier. Les étapes 1 et 2 sont faites à la lecture du nom.
/// </summary>
public static class AmbiguityResolver
{
    public static IReadOnlyList<NameReading> Resolve(IReadOnlyList<AmbiguityInput> inputs)
    {
        var evidence = inputs
            .Where(input => input.Reading.Evidence is not null)
            .GroupBy(input => (input.Folder, input.Reading.Evidence!.PatternKey))
            .ToDictionary(group => group.Key, group => group.Select(input => input.Reading.Evidence!.Order).Distinct().ToList());

        return inputs.Select(input => ResolveOne(input, evidence)).ToList();
    }

    private static NameReading ResolveOne(AmbiguityInput input, Dictionary<(string Folder, string PatternKey), List<DayMonthOrder>> evidence)
    {
        if (input.Reading.Ambiguous is not { } ambiguous)
        {
            return input.Reading;
        }

        if (evidence.TryGetValue((input.Folder, ambiguous.PatternKey), out var orders) && orders.Count == 1)
        {
            return Settle(input.Reading, orders[0] == DayMonthOrder.DayFirst ? ambiguous.DayFirst : ambiguous.MonthFirst);
        }

        var dayFirstMatches = input.OtherDates.Any(date => date.WallClock.Date == ambiguous.DayFirst.WallClock.Date);
        var monthFirstMatches = input.OtherDates.Any(date => date.WallClock.Date == ambiguous.MonthFirst.WallClock.Date);
        if (dayFirstMatches != monthFirstMatches)
        {
            return Settle(input.Reading, dayFirstMatches ? ambiguous.DayFirst : ambiguous.MonthFirst);
        }

        return input.Reading;
    }

    private static NameReading Settle(NameReading reading, DateCandidate date) => reading with { Date = date, Ambiguous = null };
}
