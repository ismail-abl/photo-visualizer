namespace PhotoApp.Core.Profiles;

/// <summary>Sens de l'heure écrite dans le nom : heure locale (cas général) ou UTC (epoch, Pixel).</summary>
public enum NameTimeSemantics
{
    Local,
    Utc
}
