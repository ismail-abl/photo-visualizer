namespace PhotoApp.Core.Models;

public sealed record PreviousOutput(long SizeBytes, long LastWriteUtcTicks, string TargetName);
