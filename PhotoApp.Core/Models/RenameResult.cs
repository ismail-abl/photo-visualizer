namespace PhotoApp.Core.Models;

public sealed record RenameResult(
    string SourcePath,
    string TargetPath,
    bool Success,
    string? ErrorMessage = null);