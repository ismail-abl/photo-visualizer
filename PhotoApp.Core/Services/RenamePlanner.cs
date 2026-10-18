using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public sealed class RenamePlanner
{
    public IReadOnlyList<RenamePlan> PlanRenames(
        IReadOnlyList<PlanInput> inputs,
        IReadOnlyDictionary<string, PreviousOutput> previous,
        IEnumerable<string> existingTargetNames,
        string outputExtension = ".jpg")
    {
        var allocator = new NameAllocator(existingTargetNames.Concat(previous.Values.Select(p => p.TargetName)));
        var plans = new List<RenamePlan>(inputs.Count);

        // Ordre chronologique puis nom de fichier (le compteur WhatsApp WAnnnn en fait partie) : stable d'un passage à l'autre.
        var ordered = inputs
            .OrderBy(i => i.Date.WallClock)
            .ThenBy(i => i.Date.HasTime)
            .ThenBy(i => Path.GetFileName(i.SourcePath), StringComparer.Ordinal)
            .ThenBy(i => i.SourcePath, StringComparer.Ordinal);

        foreach (var input in ordered)
        {
            if (previous.TryGetValue(input.SourcePath, out var done) &&
                done.SizeBytes == input.SizeBytes &&
                done.LastWriteUtcTicks == input.LastWriteUtcTicks)
            {
                plans.Add(new RenamePlan(input.SourcePath, done.TargetName, input.Date, input.SizeBytes, input.LastWriteUtcTicks, AlreadyDone: true));
                continue;
            }

            plans.Add(new RenamePlan(
                input.SourcePath,
                allocator.Allocate(input.Date, outputExtension),
                input.Date,
                input.SizeBytes,
                input.LastWriteUtcTicks));
        }

        return plans;
    }
}
