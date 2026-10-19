namespace PhotoApp.Core.Models;

/// <summary>
/// Métadonnées brutes d'un fichier, lues par PhotoApp.Metadata sans rien décider :
/// les dates sont déjà typées (origine, précision, décalage), le choix appartient au moteur.
/// </summary>
public sealed record RawMetadata
{
    public static RawMetadata Empty { get; } = new();

    public IReadOnlyList<DateCandidate> Dates { get; init; } = [];

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Software { get; init; }

    /// <summary>Identifiant Apple qui relie la photo et la vidéo d'une Live Photo.</summary>
    public string? ContentIdentifier { get; init; }

    /// <summary>Extension du vrai format détecté (« jpg », « heic », « mov »…), sans point ; null si inconnu.</summary>
    public string? DetectedExtension { get; init; }

    /// <summary>Message d'erreur quand le fichier est illisible : la décision sera « lecture impossible ».</summary>
    public string? ReadError { get; init; }
}
