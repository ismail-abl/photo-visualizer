using System.Globalization;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Models;

public sealed class ImageDateMetadata
{
    public ImageDateMetadata(
        DateCandidate? native,
        DateCandidate? fileName,
        DateCandidate? fileSystem,
        DateCandidate? retained,
        ImageDateSource retainedSource,
        string? normalizedFileNamePreview)
    {
        NativeCandidate = native;
        FileNameCandidate = fileName;
        FileSystemCandidate = fileSystem;
        RetainedCandidate = retained;
        RetainedSource = retainedSource;
        NormalizedFileNamePreview = normalizedFileNamePreview;
    }

    public DateCandidate? NativeCandidate { get; }

    public DateCandidate? FileNameCandidate { get; }

    public DateCandidate? FileSystemCandidate { get; }

    public DateCandidate? RetainedCandidate { get; }

    public ImageDateSource RetainedSource { get; }

    public bool RetainedDateHasTime => RetainedCandidate?.HasTime ?? false;

    public string? NormalizedFileNamePreview { get; }

    public bool IsReliable => DateSelector.IsReliable(RetainedSource);

    public string StatusText => RetainedSource switch
    {
        ImageDateSource.None => "À vérifier : aucune date trouvée",
        ImageDateSource.FileSystem => "À vérifier : date non fiable (date du fichier), non appliquée",
        _ => string.Empty
    };

    public string RetainedDateText => RetainedCandidate is { } retained
        ? retained.WallClock.ToString(retained.HasTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "—";

    public string RetainedSourceText => RetainedSource switch
    {
        ImageDateSource.NativeMetadata => "Métadonnées natives",
        ImageDateSource.FileName => "Nom de fichier",
        ImageDateSource.FileSystem => "Système de fichiers",
        _ => "Aucune"
    };
}
