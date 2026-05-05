using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public sealed class RenamePlanner
{
    public IReadOnlyList<RenamePlan> PlanRenames(
        IReadOnlyList<(string sourcePath, DateTimeOffset retainedDate, bool hasTime, string extension)> imageData,
        string outputFolder)
    {
        var plans = new List<RenamePlan>();

        foreach (var (sourcePath, retainedDate, hasTime, extension) in imageData)
        {
            var targetName = NormalizedFileNameBuilder.Build(retainedDate, hasTime, extension, sourcePath);
            var plan = new RenamePlan(sourcePath, targetName, retainedDate, hasTime);
            plans.Add(plan);
        }

        // Handle collisions: group by target name
        var groups = plans.GroupBy(p => p.TargetName).ToDictionary(g => g.Key, g => g.ToList());
        var finalPlans = new List<RenamePlan>();

        foreach (var group in groups)
        {
            if (group.Value.Count == 1)
            {
                finalPlans.Add(group.Value[0]);
            }
            else
            {
                // Collision: add hash to each
                foreach (var plan in group.Value)
                {
                    var hash = GenerateShortHash(plan.SourcePath);
                    var baseName = plan.HasTime
                        ? $"IMG-{plan.RetainedDate.DateTime:yyyyMMdd-HHmmss}"
                        : $"IMG-{plan.RetainedDate.DateTime:yyyyMMdd}";
                    var ext = Path.GetExtension(plan.TargetName).ToLowerInvariant();
                    var newTargetName = $"{baseName}-{hash}{ext}";
                    var newPlan = plan with { TargetName = newTargetName };
                    finalPlans.Add(newPlan);
                }
            }
        }

        return finalPlans;
    }

    private static string GenerateShortHash(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 6).ToLowerInvariant();
    }
}