using PhotoApp.Cli;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class JournalCommandTests
{
    [Fact]
    public void Run_WithoutBatches_SaysSo()
    {
        using var temp = new TempDirectory();
        var output = new StringWriter();

        var exitCode = new JournalCommand(PathsUnder(temp.Combine("data")), output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Aucun lot", output.ToString());
    }

    [Fact]
    public void Run_ListsTheBatches_WithTheirCounts()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Terminé", output.ToString());
        Assert.Contains("2 fait(s), 0 échec(s), 0 écarté(s)", output.ToString());
    }

    [Fact]
    public void Run_ListsAnUndoneBatch_WithItsUndoneCount()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        new AnnuleCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty)).Run(1, assumeYes: true);
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Annulé", output.ToString());
        Assert.Contains("0 fait(s), 0 échec(s), 0 écarté(s), 2 annulé(s)", output.ToString());
        Assert.DoesNotContain("non restauré", output.ToString());
    }

    [Fact]
    public void Run_ListsABatchWithAFileThatCouldNotBeRestored_WithItsCount()
    {
        using var temp = new TempDirectory();
        var paths = PathsUnder(temp.Combine("data"));
        var photos = Photos(temp);
        var journal = OpenJournal(paths);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);
        File.WriteAllText(target, "retouchée");   // l'annulation laissera le fichier en place
        new BatchUndo(journal).Undo(batch);
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Terminé", output.ToString());
        Assert.Contains("0 fait(s), 0 échec(s), 0 écarté(s), 1 non restauré(s)", output.ToString());
        Assert.DoesNotContain("annulé", output.ToString());
    }

    [Fact]
    public void Run_ListsBothCounts_WhenABatchIsPartiallyUndone()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        File.WriteAllText(Path.Combine(photos, "IMG-20230304-132015.jpg"), "retouchée");
        new AnnuleCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty)).Run(1, assumeYes: true);
        var output = new StringWriter();

        new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Contains("0 fait(s), 0 échec(s), 0 écarté(s), 1 annulé(s), 1 non restauré(s)", output.ToString());
    }

    [Fact]
    public void Run_ListsACompletedBatch_WithoutUndoCounters()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        var output = new StringWriter();

        new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.DoesNotContain("annulé", output.ToString());
        Assert.DoesNotContain("non restauré", output.ToString());
    }

    [Fact]
    public void Run_WithALot_ShowsEachFileWithItsStateAndError()
    {
        using var temp = new TempDirectory();
        var paths = PathsUnder(temp.Combine("data"));
        var photos = Photos(temp);
        var journal = OpenJournal(paths);
        var source = WriteFile(photos, "a.jpg");
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));
        File.WriteAllText(source, "modifié");   // le contrôle préalable l'écartera
        new InPlaceRenamer(journal).Run(batch);
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(batch);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("a.jpg", output.ToString());
        Assert.Contains("IMG-1.jpg", output.ToString());
        Assert.Contains("écarté", output.ToString());
        Assert.Contains("Fichier modifié depuis le plan", output.ToString());
    }

    [Fact]
    public void Run_UnknownLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new JournalCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(42);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Lot 42 introuvable", error.ToString());
    }

    [Fact]
    public void Run_MentionsAnUnfinishedBatch()
    {
        using var temp = new TempDirectory();
        var paths = PathsUnder(temp.Combine("data"));
        var journal = OpenJournal(paths);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [@"C:\photos"], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var output = new StringWriter();

        new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Contains($"photoapp reprend {stuck}", output.ToString());
    }
}
