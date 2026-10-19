using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Operations;

public sealed class ConversionService
{
    private readonly BatchRenameExecutor _executor;

    public ConversionService()
        : this(new BatchRenameExecutor())
    {
    }

    public ConversionService(BatchRenameExecutor executor)
    {
        _executor = executor;
    }

    public ConversionSummary Run(
        IReadOnlyList<ImageFileInfo> images,
        string outputFolder,
        string manifestPath,
        int jpegQuality,
        bool deleteOriginals)
    {
        Directory.CreateDirectory(outputFolder);

        var manifest = ProcessedManifest.Load(manifestPath);
        manifest.PruneMissingTargets(outputFolder);

        var inputs = new List<PlanInput>();
        var skippedUnreliable = 0;
        var skippedUnsupported = 0;

        foreach (var image in images)
        {
            // Seules les dates Sûre et Probable sont appliquées ; Incertaine et Aucune restent « À vérifier ».
            if (image.Decision is not { IsApplicable: true, Date: { } date })
            {
                skippedUnreliable++;
                continue;
            }

            if (!ImageConverter.CanConvert(image.Extension))
            {
                skippedUnsupported++;
                continue;
            }

            inputs.Add(new PlanInput(image.FullPath, date, image.FileSizeBytes, File.GetLastWriteTimeUtc(image.FullPath).Ticks));
        }

        var existingNames = Directory.EnumerateFiles(outputFolder).Select(path => Path.GetFileName(path)).ToList();
        var plans = new RenamePlanner().PlanRenames(inputs, manifest.ToPrevious(), existingNames);

        IReadOnlyList<RenameResult> results;
        try
        {
            results = _executor.Execute(plans, outputFolder, jpegQuality, deleteOriginals, manifest);
        }
        finally
        {
            // Même si le lot s'interrompt, ce qui a réussi reste mémorisé.
            manifest.Save(manifestPath);
        }

        return new ConversionSummary(
            Converted: results.Count(r => r.Success && !r.Skipped),
            AlreadyDone: results.Count(r => r.Skipped),
            SkippedUnreliableDate: skippedUnreliable,
            SkippedUnsupportedFormat: skippedUnsupported,
            Failed: results.Count(r => !r.Success),
            FirstError: results.FirstOrDefault(r => !r.Success)?.ErrorMessage);
    }
}
