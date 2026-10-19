namespace PhotoApp.Core.Normalization;

/// <summary>Empreinte du contenu d'un fichier : dernier critère d'ordre des collisions. Implémentée hors de Core (E/S).</summary>
public interface IContentHasher
{
    string Hash(string fullPath);
}
