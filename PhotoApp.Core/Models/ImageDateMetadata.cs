using System.Globalization;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Models;

public sealed class ImageDateMetadata
{
    public ImageDateMetadata(
        ImageDateCandidate? native,
        ImageDateCandidate? fileName,
        ImageDateCandidate? fileSystem,
        ImageDateCandidate? retained,
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

    public ImageDateCandidate? NativeCandidate { get; }

    public ImageDateCandidate? FileNameCandidate { get; }

    public ImageDateCandidate? FileSystemCandidate { get; }

    public ImageDateCandidate? RetainedCandidate { get; }

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
