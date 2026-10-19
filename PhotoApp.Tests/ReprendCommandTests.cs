using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class ReprendCommandTests
{
    private static void InterruptedRename(AppDataPaths paths, string photos)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null), cancellation.Token);
    }

    [Fact]
    public void Run_FinishesAnInterruptedBatch()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        InterruptedRename(paths, photos);
        var output = new StringWriter();

        var exitCode = new ReprendCommand(paths, output, new StringWriter()).Run(1);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.Contains("Lot 1 : 2 renommé(s)", output.ToString());
        Assert.Equal(BatchState.Completed, OpenJournal(paths).GetBatch(1)!.State);
    }

    [Fact]
    public void Run_OnACompletedBatch_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        var error = new StringWriter();

        var exitCode = new ReprendCommand(paths, new StringWriter(), error).Run(1);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("déjà terminé", error.ToString());
    }

    [Fact]
    public void Run_UnknownLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new ReprendCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(7);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Lot 7 introuvable", error.ToString());
    }
}
