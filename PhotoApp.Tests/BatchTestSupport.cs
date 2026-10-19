using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

/// <summary>Aides communes aux tests d'exécution, de reprise et d'annulation.</summary>
internal static class BatchTestSupport
{
    public static readonly DateTimeOffset Decided = new(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1));

    public static SqliteBatchJournal NewJournal(TempDirectory temp) => new(new CatalogDatabase(temp.Combine("catalog.db")));

    public static string Photos(TempDirectory temp)
    {
        var folder = temp.Combine("photos");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string WriteFile(string folder, string name, string content = "contenu")
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    public static BatchItemDraft DraftFor(string source, string target, int rank, int group = 1, ItemRole role = ItemRole.Media)
    {
        var info = new FileInfo(source);
        return new BatchItemDraft(rank, group, role, source, target, Decided, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
    }

    public static long CreateBatch(IBatchJournal journal, params BatchItemDraft[] drafts) =>
        journal.CreateBatch(DateTime.UtcNow, [Path.GetDirectoryName(drafts[0].SourcePath)!], drafts);

    /// <summary>Taille, contenu, dates de création et de modification de chaque fichier : la preuve d'un état identique.</summary>
    public static Dictionary<string, (long Size, string Hash, DateTime Creation, DateTime LastWrite)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(folder, path),
            path => (new FileInfo(path).Length,
                     Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                     File.GetCreationTimeUtc(path),
                     File.GetLastWriteTimeUtc(path)));

    /// <summary>Un <see cref="IProgress{T}"/> synchrone (contrairement à <see cref="Progress{T}"/>, qui poste le rappel).</summary>
    public sealed class SyncProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }
}
