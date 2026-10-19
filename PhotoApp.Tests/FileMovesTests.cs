namespace PhotoApp.Tests;

public sealed class FileMovesTests
{
    [Fact]
    public void Move_RenamesTheFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "contenu");

        FileMoves.Move(temp.Combine("a.jpg"), temp.Combine("b.jpg"));

        Assert.False(File.Exists(temp.Combine("a.jpg")));
        Assert.Equal("contenu", File.ReadAllText(temp.Combine("b.jpg")));
    }

    [Fact]
    public void Move_ToAnExistingFile_Throws_AndKeepsBothFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "A");
        File.WriteAllText(temp.Combine("b.jpg"), "B");

        Assert.Throws<IOException>(() => FileMoves.Move(temp.Combine("a.jpg"), temp.Combine("b.jpg")));

        Assert.Equal("A", File.ReadAllText(temp.Combine("a.jpg")));
        Assert.Equal("B", File.ReadAllText(temp.Combine("b.jpg")));
    }

    [Fact]
    public void Move_CaseOnlyChange_RenamesTheFile_AndLeavesNoTemporaryFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "contenu");

        FileMoves.Move(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"));

        var names = Directory.GetFiles(temp.FullPath).Select(Path.GetFileName).ToList();
        Assert.Equal(["IMG-1.jpg"], names);
    }

    // Déplacement injecté : les appels listés par `failingCalls` (numérotés à partir de 1) lèvent `failure`, les autres déplacent réellement.
    // `onTemporaryName` reçoit le nom voisin que Move a choisi (cible du premier appel).
    private static Action<string, string> MoverFailingOn(Exception failure, HashSet<int> failingCalls, Action<string> onTemporaryName)
    {
        var calls = 0;
        return (source, target) =>
        {
            calls++;
            if (calls == 1)
            {
                onTemporaryName(target);
            }

            if (failingCalls.Contains(calls))
            {
                throw failure;
            }

            File.Move(source, target, overwrite: false);
        };
    }

    [Fact]
    public void Move_CaseOnlyChange_WhenTheSecondStepFails_PutsTheFileBackAndRethrowsTheOriginalException()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "contenu");
        var failure = new IOException("violation de partage simulée");
        var mover = MoverFailingOn(failure, [2], _ => { });

        var thrown = Assert.Throws<IOException>(() => FileMoves.Move(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"), mover));

        Assert.Same(failure, thrown);
        var names = Directory.GetFiles(temp.FullPath).Select(Path.GetFileName).ToList();
        Assert.Equal(["IMG-1.JPG"], names);
        Assert.Equal("contenu", File.ReadAllText(temp.Combine("IMG-1.JPG")));
    }

    [Fact]
    public void Move_CaseOnlyChange_WhenTheRollbackFailsToo_ThrowsAnIOExceptionNamingTheTemporaryFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "contenu");
        var failure = new IOException("violation de partage simulée");
        string? temporary = null;
        var mover = MoverFailingOn(failure, [2, 3], name => temporary = name);

        var thrown = Assert.Throws<IOException>(() => FileMoves.Move(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"), mover));

        Assert.NotNull(temporary);
        Assert.Contains(temporary, thrown.Message);
        Assert.Same(failure, thrown.InnerException);
        Assert.True(File.Exists(temporary));
        Assert.Equal("contenu", File.ReadAllText(temporary));
        Assert.Equal([temporary], Directory.GetFiles(temp.FullPath));
    }
}
