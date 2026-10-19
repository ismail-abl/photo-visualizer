namespace PhotoApp.Core.Models;

public sealed record PlanInput(string SourcePath, DateCandidate Date, long SizeBytes, long LastWriteUtcTicks);
