using PhotoApp.Core;

namespace PhotoApp.Tests;

/// <summary>Aides communes aux tests des commandes de la CLI.</summary>
internal static class CliTestSupport
{
    public static readonly DateTime Clock = new(2026, 10, 19, 10, 15, 0);

    public static AppDataPaths PathsUnder(string root) =>
        AppDataPaths.Resolve(PlatformKind.Windows, name => name == "LOCALAPPDATA" ? root : null, root);

    public static SqliteBatchJournal OpenJournal(AppDataPaths paths)
    {
        paths.EnsureCreated();
        return new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
    }

    /// <summary>Deux fichiers à renommer (holiday.jpg : Probable, IMG-20210915-WA0005.png : Sûre) et un fichier sans date.</summary>
    public static string Library(TempDirectory temp)
    {
        var photos = BatchTestSupport.Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));
        return photos;
    }
}
