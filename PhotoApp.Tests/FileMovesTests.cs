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
}
