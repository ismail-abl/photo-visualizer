namespace PhotoApp.Core.Models;

public sealed record RenamePlan(
    string SourcePath,
    string TargetName,
    DateCandidate Date,
    long SizeBytes,
    long LastWriteUtcTicks,
    bool AlreadyDone = false);
