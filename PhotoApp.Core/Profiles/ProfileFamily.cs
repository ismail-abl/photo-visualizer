namespace PhotoApp.Core.Profiles;

/// <summary>Famille d'une source : elle décide des règles qui s'appliquent (spec de phase 1, §5.4).</summary>
public enum ProfileFamily
{
    Camera,
    Messaging,
    Screenshot,
    Export
}
