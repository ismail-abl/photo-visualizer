using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class AnnuleCommandTests
{
    private static void Rename(AppDataPaths paths, string photos, CancellationToken cancellation = default) =>
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null), cancellation);

    private static AnnuleCommand Command(AppDataPaths paths, StringWriter output, StringWriter error, string answers = "") =>
        new(paths, output, error, new StringReader(answers));

    [Fact]
    public void Run_UndoesTheLastBatch_AndGivesBackTheOriginalState()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        Rename(paths, photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("Lot 1 : 2 restauré(s), 0 non restauré(s).", output.ToString());
        Assert.Contains("Lot annulé.", output.ToString());
        Assert.Equal(BatchState.Undone, OpenJournal(paths).GetBatch(1)!.State);
    }

    [Fact]
    public void Run_AsksForConfirmation_AndAnsweringNoChangesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        var renamed = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter(), "n\n").Run(null, assumeYes: false);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(renamed, Snapshot(photos));
        Assert.Contains("Annuler le lot 1 (2 fichier(s) à restaurer) ? [o/N]", output.ToString());
        Assert.Contains("Abandon", output.ToString());
    }

    [Fact]
    public void Run_AnsweringYes_Undoes()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        Rename(paths, photos);

        var exitCode = Command(paths, new StringWriter(), new StringWriter(), "oui\n").Run(1, assumeYes: false);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
    }

    [Fact]
    public void Run_WithNothingToUndo_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Aucun lot à annuler", error.ToString());
    }

    [Fact]
    public void Run_UnknownOrAlreadyUndoneLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        Command(paths, new StringWriter(), new StringWriter()).Run(1, assumeYes: true);
        var error = new StringWriter();

        Assert.Equal(ExitCodes.InvalidArguments, Command(paths, new StringWriter(), error).Run(1, assumeYes: true));
        Assert.Contains("déjà annulé", error.ToString());
        Assert.Equal(ExitCodes.InvalidArguments, Command(paths, new StringWriter(), error).Run(99, assumeYes: true));
        Assert.Contains("Lot 99 introuvable", error.ToString());
    }

    [Fact]
    public void Run_WithoutAnId_WhileABatchIsUnfinished_AsksForAnExplicitChoice()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Rename(paths, photos, cancellation.Token);   // lot 1 interrompu, rien de renommé
        var error = new StringWriter();

        var exitCode = Command(paths, new StringWriter(), error).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("photoapp annule 1", error.ToString());
        Assert.Contains("photoapp reprend 1", error.ToString());
    }

    [Fact]
    public void Run_AFileEditedSinceTheBatch_IsLeftInPlace_AndReturnsCode3()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        File.WriteAllText(Path.Combine(photos, "IMG-20230304-132015.jpg"), "retouchée");
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.BatchIssues, exitCode);
        Assert.Equal("retouchée", File.ReadAllText(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-WA0005.png")));   // l'autre fichier est bien restauré
        Assert.Contains("Fichier modifié depuis le lot", output.ToString());
        Assert.Contains("partiellement annulé", output.ToString());
    }

    [Fact]
    public void Run_InvokesExecutionStarting_OnceAndBeforeAnyFileChanges()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        var calls = 0;
        var stillRenamed = false;

        var exitCode = Command(paths, new StringWriter(), new StringWriter(), "o\n")
            .Run(null, assumeYes: false, executionStarting: () =>
            {
                calls++;
                stillRenamed = File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg"))
                    && !File.Exists(Path.Combine(photos, "holiday.jpg"));
            });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(1, calls);
        Assert.True(stillRenamed);
        Assert.True(File.Exists(Path.Combine(photos, "holiday.jpg")));   // la restauration a bien eu lieu ensuite
    }

    [Fact]
    public void Run_WithYes_InvokesExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        var calls = 0;

        Command(paths, new StringWriter(), new StringWriter()).Run(null, assumeYes: true, executionStarting: () => calls++);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Run_DeclinedConfirmation_DoesNotInvokeExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        var calls = 0;

        Command(paths, new StringWriter(), new StringWriter(), "n\n").Run(null, assumeYes: false, executionStarting: () => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Run_WithNothingToUndo_DoesNotInvokeExecutionStarting()
    {
        using var temp = new TempDirectory();
        var calls = 0;

        Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter()).Run(null, assumeYes: true, executionStarting: () => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Run_ListsTheWarnings_OfFilesUndoneWithAReserve_AndStaysAtCode0()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        // Un premier passage a restauré holiday.jpg par son nom, sans ses dates d'origine : l'élément est « annulé » avec une réserve.
        var journal = OpenJournal(paths);
        var warned = journal.GetItems(1).Single(item => item.SourcePath.EndsWith("holiday.jpg", StringComparison.OrdinalIgnoreCase));
        File.Move(warned.TargetPath, warned.SourcePath);
        journal.SetItemState(warned.Id, ItemState.Undone, "nom restauré, dates d'origine non restaurées : test");
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(1, assumeYes: true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Lot 1 : 1 restauré(s), 0 non restauré(s).", output.ToString());
        Assert.Contains("1 fichier(s) traité(s) avec avertissement :", output.ToString());
        Assert.Contains("avertissement : ", output.ToString());
        Assert.Contains("nom restauré, dates d'origine non restaurées : test", output.ToString());
        Assert.Contains("Lot annulé.", output.ToString());
        Assert.Equal(BatchState.Undone, OpenJournal(paths).GetBatch(1)!.State);
    }
}
