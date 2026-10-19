using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Progression sur la sortie d'erreur, réécrite sur la même ligne tous les 100 fichiers et à la fin.</summary>
public sealed class BatchConsoleProgress(TextWriter writer, string label) : IProgress<BatchProgress>
{
    public void Report(BatchProgress value)
    {
        if (value.Processed % 100 != 0 && value.Processed != value.Total)
        {
            return;
        }

        writer.Write($"\r{label} : {value.Processed} / {value.Total}");
    }
}
