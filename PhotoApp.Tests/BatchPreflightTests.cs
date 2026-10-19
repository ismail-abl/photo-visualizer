namespace PhotoApp.Tests;

public sealed class BatchPreflightTests
{
    private static PreflightEntry EntryFor(string source, string target, int rank = 0, int group = 1)
    {
        var info = new FileInfo(source);
        return new PreflightEntry(rank, group, source, target, info.Length, info.LastWriteTimeUtc);
    }

    [Fact]
    public void Check_UntouchedFileWithAFreeTarget_PassesTheChecks()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");

        Assert.Null(BatchPreflight.Check(EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_MissingSource_IsRejected()
    {
        using var temp = new TempDirectory();
        var entry = new PreflightEntry(0, 1, temp.Combine("absent.jpg"), temp.Combine("IMG-1.jpg"), 1, DateTime.UtcNow);

        Assert.Equal("Fichier introuvable", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_SourceChangedSinceThePlan_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"));

        File.WriteAllText(temp.Combine("a.jpg"), "contenu plus long");

        Assert.Equal("Fichier modifié depuis le plan", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_TargetAlreadyTaken_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        File.WriteAllText(temp.Combine("IMG-1.jpg"), "autre");

        Assert.Equal("Le nom cible existe déjà", BatchPreflight.Check(EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_CaseOnlyChange_IsNotATargetCollision()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "x");

        Assert.Null(BatchPreflight.Check(EntryFor(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_LockedFile_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"));

        using var held = new FileStream(temp.Combine("a.jpg"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("Fichier verrouillé", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_TooLongTargetPath_IsRejectedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine(new string('n', 260) + ".jpg"));

        Assert.Equal("Chemin trop long", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Evaluate_AGroupIsAllOrNothing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        File.WriteAllText(temp.Combine("a.xmp"), "x");
        File.WriteAllText(temp.Combine("b.jpg"), "x");
        File.WriteAllText(temp.Combine("IMG-1.xmp"), "déjà là");

        var reasons = BatchPreflight.Evaluate(
        [
            EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"), rank: 0, group: 1),
            EntryFor(temp.Combine("a.xmp"), temp.Combine("IMG-1.xmp"), rank: 1, group: 1),
            EntryFor(temp.Combine("b.jpg"), temp.Combine("IMG-2.jpg"), rank: 2, group: 2)
        ]);

        Assert.Equal([0, 1], reasons.Keys.Order());
        Assert.Equal("Le nom cible existe déjà", reasons[1]);
        Assert.StartsWith("Groupe écarté : a.xmp", reasons[0]);
        Assert.False(reasons.ContainsKey(2));
    }
}
