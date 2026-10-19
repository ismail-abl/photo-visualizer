using System.Security.Cryptography;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Metadata;

/// <summary>Empreinte SHA-256 du contenu : dernier critère d'ordre des collisions de noms.</summary>
public sealed class Sha256ContentHasher : IContentHasher
{
    public string Hash(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier devenu illisible : l'ordre retombe sur le chemin, l'analyse continue.
            return string.Empty;
        }
    }
}
