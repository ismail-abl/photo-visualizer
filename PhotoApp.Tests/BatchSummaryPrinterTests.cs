using PhotoApp.Cli;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class BatchSummaryPrinterTests
{
    private const string Warning = "dates non appliquées : test";

    /// <summary>Un lot de <paramref name="count"/> fichiers (a0.jpg, a1.jpg, …), tous en attente.</summary>
    private static (SqliteBatchJournal Journal, long BatchId, IReadOnlyList<BatchItem> Items) NewBatch(TempDirectory temp, int count = 2)
    {
        var photos = Photos(temp);
        var drafts = Enumerable.Range(0, count)
            .Select(index => DraftFor(
                WriteFile(photos, $"a{index}.jpg"),
                Path.Combine(photos, $"IMG-20230304-1320{index:00}.jpg"),
                rank: index,
                group: index + 1))
            .ToArray();
        var journal = NewJournal(temp);
        var batchId = CreateBatch(journal, drafts);
        return (journal, batchId, journal.GetItems(batchId));
    }

    private static string RunSummary(SqliteBatchJournal journal, long batchId)
    {
        var output = new StringWriter();
        BatchSummaryPrinter.PrintRun(output, journal, new BatchRunSummary(batchId, journal.GetBatch(batchId)!.Counts, Cancelled: false));
        return output.ToString();
    }

    [Fact]
    public void PrintRun_ListsTheDoneItemsThatCarryAWarning_AndOnlyThose()
    {
        using var temp = new TempDirectory();
        var (journal, batchId, items) = NewBatch(temp);
        journal.SetItemDone(items[0].Id, 7, DateTime.UtcNow, Warning);
        journal.SetItemDone(items[1].Id, 7, DateTime.UtcNow);

        var text = RunSummary(journal, batchId);

        var warningLines = text.Split(Environment.NewLine).Where(line => line.TrimStart().StartsWith("avertissement", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(warningLines);
        Assert.Contains(items[0].SourcePath, line);
        Assert.Contains(Warning, line);
        Assert.DoesNotContain(items[1].SourcePath, text);
        Assert.Contains("Lot 1 : 2 renommé(s), 0 en échec, 0 écarté(s).", text);   // la phrase de bilan est inchangée
    }

    [Fact]
    public void PrintRun_WithoutAnyWarning_PrintsNoWarningLine()
    {
        using var temp = new TempDirectory();
        var (journal, batchId, items) = NewBatch(temp);
        journal.SetItemDone(items[0].Id, 7, DateTime.UtcNow);
        journal.SetItemDone(items[1].Id, 7, DateTime.UtcNow);

        var text = RunSummary(journal, batchId);

        Assert.DoesNotContain("avertissement", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lot 1 : 2 renommé(s), 0 en échec, 0 écarté(s).", text);
    }

    [Fact]
    public void PrintRun_AFailedItem_IsAProblem_NotAWarning()
    {
        using var temp = new TempDirectory();
        var (journal, batchId, items) = NewBatch(temp);
        journal.SetItemDone(items[0].Id, 7, DateTime.UtcNow);
        journal.SetItemState(items[1].Id, ItemState.Failed, "nom occupé");

        var text = RunSummary(journal, batchId);

        Assert.Contains("échec : " + items[1].SourcePath + " — nom occupé", text);
        Assert.DoesNotContain("avertissement", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintWarnings_AnUndoneItemWithAMessage_IsListedToo()
    {
        using var temp = new TempDirectory();
        var (journal, batchId, items) = NewBatch(temp);
        journal.SetItemState(items[0].Id, ItemState.Undone, "dates d'origine non restaurées : test");
        journal.SetItemState(items[1].Id, ItemState.Undone);
        var output = new StringWriter();

        BatchSummaryPrinter.PrintWarnings(output, journal, batchId);

        var text = output.ToString();
        Assert.Contains("avertissement : " + items[0].SourcePath + " — dates d'origine non restaurées : test", text);
        Assert.DoesNotContain(items[1].SourcePath, text);
    }

    [Fact]
    public void PrintWarnings_ListsAtMostTwentyItems_ThenPointsToTheJournal()
    {
        using var temp = new TempDirectory();
        var (journal, batchId, items) = NewBatch(temp, count: 23);
        foreach (var item in items)
        {
            journal.SetItemDone(item.Id, 7, DateTime.UtcNow, Warning);
        }

        var output = new StringWriter();

        BatchSummaryPrinter.PrintWarnings(output, journal, batchId);

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(20, lines.Count(line => line.TrimStart().StartsWith("avertissement :", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.Contains("… et 3 autre(s) : photoapp journal --lot " + batchId));
    }
}
