namespace PhotoApp.Core.Models;

public sealed record PlanInput(string SourcePath, ImageDateCandidate Date, long SizeBytes, long LastWriteUtcTicks);
