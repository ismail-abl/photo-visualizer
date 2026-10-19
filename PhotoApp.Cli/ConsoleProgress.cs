using PhotoApp.Catalog;

namespace PhotoApp.Cli;

/// <summary>Progression sur la sortie d'erreur, réécrite sur la même ligne tous les 100 fichiers et à la fin.</summary>
public sealed class ConsoleProgress(TextWriter writer) : IProgress<AnalysisProgress>
{
    private readonly object _gate = new();

    public void Report(AnalysisProgress value)
    {
        if (value.Done % 100 != 0 && value.Done != value.Total)
        {
            return;
        }

        lock (_gate)
        {
            writer.Write($"\rLecture des métadonnées : {value.Done} / {value.Total}");
        }
    }
}
