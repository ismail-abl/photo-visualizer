using System.Text.Json;
using PhotoApp.Core.Models;

namespace PhotoApp.Operations;

public sealed record ManifestEntry(string SourcePath, long SizeBytes, long LastWriteUtcTicks, string TargetName);

/// <summary>Mémoire des fichiers déjà convertis pour un dossier de sortie. Stockée dans les données de l'application.</summary>
public sealed class ProcessedManifest
{
    private readonly Dictionary<string, ManifestEntry> _entries;

    private ProcessedManifest(Dictionary<string, ManifestEntry> entries)
    {
        _entries = entries;
    }

    public int Count => _entries.Count;

    public static ProcessedManifest Empty() => new(new Dictionary<string, ManifestEntry>(StringComparer.Ordinal));

    public static ProcessedManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path)) ?? [];
            var entries = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                entries[entry.SourcePath] = entry;
            }

            return new ProcessedManifest(entries);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Le manifeste est illisible : {path}", ex);
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries.Values.OrderBy(e => e.SourcePath, StringComparer.Ordinal)));
        File.Move(temporary, path, overwrite: true);
    }

    public void Record(ManifestEntry entry) => _entries[entry.SourcePath] = entry;

    public void PruneMissingTargets(string outputFolder)
    {
        foreach (var entry in _entries.Values.ToList())
        {
            if (!File.Exists(Path.Combine(outputFolder, entry.TargetName)))
            {
                _entries.Remove(entry.SourcePath);
            }
        }
    }

    public IReadOnlyDictionary<string, PreviousOutput> ToPrevious() =>
        _entries.ToDictionary(
            pair => pair.Key,
            pair => new PreviousOutput(pair.Value.SizeBytes, pair.Value.LastWriteUtcTicks, pair.Value.TargetName),
            StringComparer.Ordinal);
}
