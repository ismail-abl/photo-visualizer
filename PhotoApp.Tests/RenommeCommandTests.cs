using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class RenommeCommandTests
{
    private static RenommeRequest Request(string photos, bool apply = false, bool yes = false, int? slice = null) =>
        new([photos], [], apply, yes, slice);

    private static RenommeCommand Command(AppDataPaths paths, StringWriter output, StringWriter error, string answers = "") =>
        new(paths, output, error, new StringReader(answers), () => Clock);

    [Fact]
    public void DryRun_WritesThePlan_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("À renommer : 2 fichier(s)", output.ToString());
        Assert.Contains("Aucune modification", output.ToString());
        Assert.True(File.Exists(Path.Combine(paths.ReportsFolder, "renomme-20261019-101500.csv")));
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void Apply_WithYes_RenamesInPlace_AndRecordsTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.True(File.Exists(Path.Combine(photos, "blank.jpg")));
        Assert.Contains("Lot 1 : 2 renommé(s), 0 en échec, 0 écarté(s).", output.ToString());
        var batch = Assert.Single(OpenJournal(paths).ListBatches());
        Assert.Equal(BatchState.Completed, batch.State);
        Assert.Equal(2, batch.Counts.Done);
    }

    [Fact]
    public void Apply_AnsweringYes_RenamesInPlace()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var output = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), output, new StringWriter(), "o\n").Run(Request(photos, apply: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.Contains("Renommer 2 fichier(s) sur place ? [o/N]", output.ToString());
    }

    [Fact]
    public void Apply_AnsweringNo_ChangesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter(), "n\n").Run(Request(photos, apply: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("Abandon", output.ToString());
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void Apply_WithAnUnfinishedBatch_IsRefused()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var journal = OpenJournal(paths);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [photos], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var before = Snapshot(photos);
        var error = new StringWriter();

        var exitCode = Command(paths, new StringWriter(), error).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains($"photoapp reprend {stuck}", error.ToString());
    }

    [Fact]
    public void Apply_Cancelled_LeavesAnInterruptedBatch_AndReturnsCode3()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true), cancellation.Token);

        Assert.Equal(ExitCodes.BatchIssues, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Interrupted, Assert.Single(OpenJournal(paths).ListBatches()).State);
        Assert.Contains("photoapp reprend 1", output.ToString());
    }

    [Fact]
    public void Apply_InvokesExecutionStarting_OnceAndBeforeAnyFileChanges()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var calls = 0;
        var oldNameStillThere = false;
        var newNameAbsent = false;
        var noBatchYet = false;

        var exitCode = Command(paths, new StringWriter(), new StringWriter()).Run(
            Request(photos, apply: true, yes: true),
            executionStarting: () =>
            {
                calls++;
                oldNameStillThere = File.Exists(Path.Combine(photos, "holiday.jpg"));
                newNameAbsent = !File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg"));
                noBatchYet = OpenJournal(paths).ListBatches().Count == 0;
            });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(1, calls);
        Assert.True(oldNameStillThere);
        Assert.True(newNameAbsent);
        Assert.True(noBatchYet);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));   // le lot a bien été exécuté ensuite
    }

    [Fact]
    public void Apply_AfterAConfirmedPrompt_InvokesExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var calls = 0;

        Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter(), "o\n")
            .Run(Request(photos, apply: true), executionStarting: () => calls++);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void DryRun_DoesNotInvokeExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var calls = 0;

        Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(Request(photos), executionStarting: () => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void DeclinedConfirmation_DoesNotInvokeExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var calls = 0;

        Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter(), "n\n")
            .Run(Request(photos, apply: true), executionStarting: () => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void NothingToRename_DoesNotInvokeExecutionStarting()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));
        var calls = 0;

        Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(Request(photos, apply: true, yes: true), executionStarting: () => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Slice_KeepsTheFirstGroupsOnly()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var output = new StringWriter();

        Command(PathsUnder(temp.Combine("data")), output, new StringWriter()).Run(Request(photos, apply: true, yes: true, slice: 1));

        Assert.Contains("À renommer : 1 fichier(s)", output.ToString());
        Assert.True(File.Exists(Path.Combine(photos, "holiday.jpg")));   // le second groupe attend la tranche suivante
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
    }

    [Fact]
    public void NothingToRename_SaysSo_AndRecordsNoBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));
        var paths = PathsUnder(temp.Combine("data"));
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Rien à renommer.", output.ToString());
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void MissingFolder_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(Request(temp.Combine("absent")));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Dossier introuvable", error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveSlice_IsAnInvalidArgument(int slice)
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter()).Run(Request(photos, slice: slice));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }

    [Fact]
    public void ThePlanReport_IsNeverWrittenInsideAnAnalysedFolder()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var error = new StringWriter();

        // Les données de l'application sont (par erreur) sous le dossier analysé : le rapport y irait.
        var exitCode = Command(PathsUnder(photos), new StringWriter(), error).Run(Request(photos));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("dossier analysé", error.ToString());
        Assert.Equal(before, Snapshot(photos));
    }
}
