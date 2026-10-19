using System.Text;
using PhotoApp.Cli.Report;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class CsvReportWriterTests
{
    private static string[] Lines(params MediaFacts[] facts)
    {
        var writer = new StringWriter();
        CsvReportWriter.Write(writer, NormalizationEngineTests.Engine().Normalize(facts));
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static Dictionary<string, string> Row(string line) =>
        CsvReportWriter.Columns.Zip(line.Split(';')).ToDictionary(pair => pair.First, pair => pair.Second);

    [Fact]
    public void Header_ListsTheColumnsInOrder()
    {
        Assert.Equal(
            "chemin;type;format_reel;profil;date;precision;decalage;confiance;raison;nom_propose;exif;quicktime;gps;xmp;nom;date_fichier;fichiers_lies;drapeaux;erreur",
            Lines()[0]);
    }

    [Fact]
    public void Row_DescribesTheDecision()
    {
        var row = Row(Lines(NormalizationEngineTests.Facts("/p/IMG-20210915-WA0005.jpg"))[1]);

        Assert.Equal("/p/IMG-20210915-WA0005.jpg", row["chemin"]);
        Assert.Equal("photo", row["type"]);
        Assert.Equal("jpg", row["format_reel"]);
        Assert.Equal("whatsapp", row["profil"]);
        Assert.Equal("2021-09-15", row["date"]);
        Assert.Equal("jour", row["precision"]);
        Assert.Equal("", row["decalage"]);
        Assert.Equal("Sûre", row["confiance"]);
        Assert.Equal("messagerie-nom", row["raison"]);
        Assert.Equal("IMG-20210915-000000-01.jpg", row["nom_propose"]);
        Assert.Equal("2021-09-15", row["nom"]);
        Assert.Equal("2025-06-01 10:00:00 UTC", row["date_fichier"]);
        Assert.Equal("", row["erreur"]);
    }

    [Fact]
    public void Row_ShowsRejectedCandidates_LinkedFiles_AndFlags()
    {
        var reset = new DateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), DatePrecision.Second, CandidateOrigin.ExifOriginal);
        var facts = new MediaFacts(
            new ScannedMedia("/p/IMG_20230304_132015-min.jpg", 100, new DateTime(2025, 6, 1, 10, 0, 0), ["/p/IMG_20230304_132015-min.xmp"]),
            new RawMetadata { Dates = [reset] });

        var row = Row(Lines(facts)[1]);

        Assert.Equal("2000-01-01 00:00:00 (écartée)", row["exif"]);
        Assert.Equal("IMG_20230304_132015-min.xmp", row["fichiers_lies"]);
        Assert.Equal("derive", row["drapeaux"]);
    }

    [Fact]
    public void Values_WithSeparatorsOrQuotes_AreQuoted()
    {
        var line = Lines(NormalizationEngineTests.Facts("/p/a;\"b\".jpg"))[1];

        Assert.StartsWith("\"/p/a;\"\"b\"\".jpg\";", line);
    }

    [Fact]
    public void WriteFile_CreatesTheFolder_AndWritesUtf8WithBom()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("reports", "rapport.csv");

        CsvReportWriter.WriteFile(path, NormalizationEngineTests.Engine().Normalize([NormalizationEngineTests.Facts("/p/Screenshot 2025-08-27 211650.png")]));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("capture-windows", Encoding.UTF8.GetString(bytes));
    }
}
