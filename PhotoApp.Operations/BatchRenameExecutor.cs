using System.IO;
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
            // Check if target exists
            if (File.Exists(targetPath))
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, false, "Target file already exists"));
            }
            else
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, true));
            }
        }

        return results;
    }

    public IReadOnlyList<RenameResult> Execute(IReadOnlyList<RenamePlan> plans, string outputFolder, int jpegQuality, bool deleteOriginals)
    {
        var results = new List<RenameResult>();
        Directory.CreateDirectory(outputFolder);

        foreach (var plan in plans)
        {
            var result = ExecuteSingle(plan, outputFolder, jpegQuality, deleteOriginals);
            results.Add(result);
        }

        return results;
    }

    private RenameResult ExecuteSingle(RenamePlan plan, string outputFolder, int jpegQuality, bool deleteOriginal)
    {
        var targetPath = Path.Combine(outputFolder, plan.TargetName);
        var sourceExtension = Path.GetExtension(plan.SourcePath);
        var tempPath = Path.Combine(outputFolder, $"{Guid.NewGuid()}{sourceExtension}");

        try
        {
            // 1. Copy source to temp file
            File.Copy(plan.SourcePath, tempPath, overwrite: false);

            // 2. Convert to JPEG
            var jpegPath = Path.Combine(outputFolder, $"{Guid.NewGuid()}.jpg");
            _imageConverter.ConvertToJpeg(tempPath, jpegPath, jpegQuality);

            // Clean up temp
            File.Delete(tempPath);

            // 3. Apply timestamps
            _timestampApplier.ApplyTimestamps(jpegPath, plan.RetainedDate);

            // 4. Rename to final target
            File.Move(jpegPath, targetPath, overwrite: false);

            // 5. Optionally delete original
            if (deleteOriginal)
            {
                File.Delete(plan.SourcePath);
            }

            return new RenameResult(plan.SourcePath, targetPath, true);
        }
        catch (Exception ex)
        {
            // Clean up temp files on failure
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            var jpegPath = Path.Combine(outputFolder, $"{Guid.NewGuid()}.jpg");
            if (File.Exists(jpegPath))
            {
                File.Delete(jpegPath);
            }
            return new RenameResult(plan.SourcePath, targetPath, false, ex.Message);
        }
    }
}