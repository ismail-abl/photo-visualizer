using PhotoApp.Cli.Report;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class PlanCsvWriterTests
{
    [Fact]
    public void Write_ListsEveryItem_WithItsStateAndReason()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        Directory.CreateDirectory(Path.Combine(photos, "IMG-20230304-132015.jpg"));
        var results = new MediaAnalyzer().Analyze([photos], []).Results;
        var prepared = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne).Prepare(results, [photos]);
        var writer = new StringWriter();

        PlanCsvWriter.Write(writer, prepared);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("groupe;role;chemin;cible;date;etat;motif", lines[0]);
        Assert.Equal(3, lines.Length);   // en-tête + 2 éléments
        Assert.Contains(lines, line => line.Contains("IMG-20210915-WA0005.png") && line.Contains(";a-renommer;"));
        Assert.Contains(lines, line => line.Contains("holiday.jpg") && line.Contains(";ecarte;Le nom cible existe déjà"));
    }
}
