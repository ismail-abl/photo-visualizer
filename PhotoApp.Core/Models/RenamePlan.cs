namespace PhotoApp.Core.Models;

public sealed record RenamePlan(
    string SourcePath,
    string TargetName,
    DateTimeOffset RetainedDate,
    bool HasTime);