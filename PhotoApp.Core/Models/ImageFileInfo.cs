namespace PhotoApp.Core.Models;

public sealed class ImageFileInfo
{
    public ImageFileInfo(
        string fullPath,
        string fileName,
        string extension,
        long fileSizeBytes,
        ImageDateMetadata? dateMetadata = null)
    {
        FullPath = fullPath;
        FileName = fileName;
        Extension = extension;
        FileSizeBytes = fileSizeBytes;
        DateMetadata = dateMetadata;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string Extension { get; }

    public long FileSizeBytes { get; }

    public ImageDateMetadata? DateMetadata { get; }

    public string RetainedDateText => DateMetadata?.RetainedDateText ?? "—";

    public string RetainedDateSourceText => DateMetadata?.RetainedSourceText ?? "Aucune";

    public string? NormalizedFileNamePreview => DateMetadata?.NormalizedFileNamePreview;
}
