using PhotoApp.Core.Models;

namespace PhotoApp.Operations;

public sealed class BatchRenameExecutor
{
    private readonly IImageConverter _imageConverter;
    private readonly IFileTimestampApplier _timestampApplier;

    public BatchRenameExecutor()
        : this(new ImageConverter(), new FileTimestampApplier())
    {
    }

    public BatchRenameExecutor(IImageConverter imageConverter, IFileTimestampApplier timestampApplier)
    {
        _imageConverter = imageConverter;
        _timestampApplier = timestampApplier;
    }

    public IReadOnlyList<RenameResult> DryRun(IReadOnlyList<RenamePlan> plans, string outputFolder)
    {
        var results = new List<RenameResult>();
        Directory.CreateDirectory(outputFolder);

        foreach (var plan in plans)
        {
            var targetPath = Path.Combine(outputFolder, plan.TargetName);
            if (!plan.AlreadyDone && File.Exists(targetPath))
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, false, "Target file already exists"));
            }
            else
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, true, Skipped: plan.AlreadyDone));
            }
        }

        return results;
    }

    public IReadOnlyList<RenameResult> Execute(
        IReadOnlyList<RenamePlan> plans,
        string outputFolder,
        int jpegQuality,
        bool deleteOriginals,
        ProcessedManifest? manifest = null)
    {
        var results = new List<RenameResult>();
        Directory.CreateDirectory(outputFolder);

        foreach (var plan in plans)
        {
            results.Add(ExecuteSingle(plan, outputFolder, jpegQuality, deleteOriginals, manifest));
        }

        return results;
    }

    private RenameResult ExecuteSingle(RenamePlan plan, string outputFolder, int jpegQuality, bool deleteOriginal, ProcessedManifest? manifest)
    {
        var targetPath = Path.Combine(outputFolder, plan.TargetName);
        if (plan.AlreadyDone)
        {
            return new RenameResult(plan.SourcePath, targetPath, true, Skipped: true);
        }

        // Les chemins de travail sont calculés une seule fois : le nettoyage vise exactement ce qui a été créé.
        // Le préfixe ".work-" les rend invisibles au scanner.
        var workStem = Path.Combine(outputFolder, $".work-{Guid.NewGuid():N}");
        var workSource = workStem + ".source" + Path.GetExtension(plan.SourcePath);   // distinct de workJpeg même pour une source .jpg
        var workJpeg = workStem + ".jpg";

        try
        {
            File.Copy(plan.SourcePath, workSource, overwrite: false);
            _imageConverter.ConvertToJpeg(workSource, workJpeg, jpegQuality);
            _timestampApplier.ApplyTimestamps(workJpeg, plan.Date.ToInstant(TimeZoneInfo.Local));
            File.Move(workJpeg, targetPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return new RenameResult(plan.SourcePath, targetPath, false, ex.Message);
        }
        finally
        {
            DeleteIfExists(workSource);
            DeleteIfExists(workJpeg);
        }

        manifest?.Record(new ManifestEntry(plan.SourcePath, plan.SizeBytes, plan.LastWriteUtcTicks, plan.TargetName));

        if (!deleteOriginal)
        {
            return new RenameResult(plan.SourcePath, targetPath, true);
        }

        try
        {
            File.Delete(plan.SourcePath);
            return new RenameResult(plan.SourcePath, targetPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RenameResult(plan.SourcePath, targetPath, true, $"Original non supprimé : {ex.Message}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au mieux : ne doit jamais masquer l'erreur d'origine.
        }
    }
}
