using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public sealed class RenamePlanner
{
    public IReadOnlyList<RenamePlan> PlanRenames(
        IReadOnlyList<(string sourcePath, ImageDateCandidate date, string extension)> imageData,
        string outputFolder)
    {
        var plans = new List<RenamePlan>();

        foreach (var (sourcePath, date, extension) in imageData)
        {
            var targetName = NormalizedFileNameBuilder.Build(date, extension, sourcePath);
            var plan = new RenamePlan(sourcePath, targetName, date);
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
                    var baseName = plan.Date.HasTime
                        ? $"IMG-{plan.Date.WallClock:yyyyMMdd-HHmmss}"
                        : $"IMG-{plan.Date.WallClock:yyyyMMdd}";
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