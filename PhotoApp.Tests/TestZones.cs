namespace PhotoApp.Tests;

/// <summary>Fuseaux fixes, sans heure d'été : les tests ne dépendent ni du PC ni de la date.</summary>
public static class TestZones
{
    public static readonly TimeZoneInfo PlusOne =
        TimeZoneInfo.CreateCustomTimeZone("UTC+01", TimeSpan.FromHours(1), "UTC+01", "UTC+01");
}
