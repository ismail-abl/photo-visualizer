namespace PhotoApp.Core.Models;

public sealed class ImageDateMetadata
{
    public ImageDateMetadata(
        DateTimeOffset? originalDate,
        DateTimeOffset? createDate,
        DateTimeOffset? modifyDate,
        DateTimeOffset? retainedDate,
        ImageDateSource retainedSource,
        bool retainedDateHasTime,
        string? normalizedFileNamePreview)
    {
        OriginalDate = originalDate;
        CreateDate = createDate;
        ModifyDate = modifyDate;
        RetainedDate = retainedDate;
        RetainedSource = retainedSource;
        RetainedDateHasTime = retainedDateHasTime;
        NormalizedFileNamePreview = normalizedFileNamePreview;
    }

    public DateTimeOffset? OriginalDate { get; }

    public DateTimeOffset? CreateDate { get; }

    public DateTimeOffset? ModifyDate { get; }

    public DateTimeOffset? RetainedDate { get; }

    public ImageDateSource RetainedSource { get; }

    public bool RetainedDateHasTime { get; }

    public string? NormalizedFileNamePreview { get; }

    public string RetainedDateText => RetainedDate is DateTimeOffset retainedDate
        ? retainedDate.ToString(RetainedDateHasTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd")
        : "—";

    public string RetainedSourceText => RetainedSource switch
    {
        ImageDateSource.NativeMetadata => "Métadonnées natives",
        ImageDateSource.FileName => "Nom de fichier",
        ImageDateSource.FileSystem => "Système de fichiers",
        _ => "Aucune"
    };
}
