using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Ce que le moteur sait d'un fichier : le scan et les métadonnées brutes.</summary>
public sealed record MediaFacts(ScannedMedia File, RawMetadata Metadata);
