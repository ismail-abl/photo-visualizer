using PhotoApp.Cli;

namespace PhotoApp.Tests;

public sealed class SyncFolderDetectorTests
{
    [Fact]
    public void IsSynced_AFolderUnderANextcloudRoot_IsDetected()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("Nextcloud");
        var sub = Path.Combine(root, "Pictures", "2024");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(root, ".sync_1a2b3c.db"), string.Empty);

        Assert.True(SyncFolderDetector.IsSynced(sub));
        Assert.True(SyncFolderDetector.IsSynced(root));
    }

    [Fact]
    public void IsSynced_TheOlderMarkerNames_AreDetected()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("Cloud");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".nextcloudsync.log"), string.Empty);

        Assert.True(SyncFolderDetector.IsSynced(root));
    }

    [Fact]
    public void IsSynced_AnOrdinaryFolder_IsNot()
    {
        using var temp = new TempDirectory();
        var folder = temp.Combine("photos");
        Directory.CreateDirectory(folder);

        Assert.False(SyncFolderDetector.IsSynced(folder));
    }
}
