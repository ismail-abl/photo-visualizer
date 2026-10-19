using PhotoApp.Core.Models;

namespace PhotoApp.Catalog;

/// <summary>Ce que le scan a écarté, pour le résumé : fichiers et dossiers cachés, fichiers vides, dossiers inaccessibles.</summary>
public sealed record ScanStatistics(int HiddenFiles, int HiddenFolders, int EmptyFiles, int InaccessibleFolders)
{
    public static ScanStatistics None { get; } = new(0, 0, 0, 0);

    public ScanStatistics Add(ScanStatistics other) => new(
        HiddenFiles + other.HiddenFiles,
        HiddenFolders + other.HiddenFolders,
        EmptyFiles + other.EmptyFiles,
        InaccessibleFolders + other.InaccessibleFolders);
}

public sealed record ScanResult(IReadOnlyList<ScannedMedia> Media, ScanStatistics Statistics);
