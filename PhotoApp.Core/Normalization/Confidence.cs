namespace PhotoApp.Core.Normalization;

/// <summary>Sûre et Probable sont appliquées ; Incertaine part dans « À vérifier » ; Aucune n'invente jamais de date.</summary>
public enum Confidence
{
    None = 0,
    Uncertain = 1,
    Probable = 2,
    Certain = 3
}
