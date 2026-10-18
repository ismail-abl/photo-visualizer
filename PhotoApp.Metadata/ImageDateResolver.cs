using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Metadata;

public sealed class ImageDateResolver
{
    private readonly NativeImageMetadataReader _nativeImageMetadataReader;
    private readonly FileNameDateParser _fileNameDateParser;
    private readonly FileSystemDateReader _fileSystemDateReader;

    public ImageDateResolver()
        : this(new NativeImageMetadataReader(), new FileNameDateParser(), new FileSystemDateReader())
    {
    }

    public ImageDateResolver(
        NativeImageMetadataReader nativeImageMetadataReader,
        FileNameDateParser fileNameDateParser,
        FileSystemDateReader fileSystemDateReader)
    {
        _nativeImageMetadataReader = nativeImageMetadataReader;
        _fileNameDateParser = fileNameDateParser;
        _fileSystemDateReader = fileSystemDateReader;
    }

    public ImageDateMetadata Resolve(string filePath)
    {
        var nativeMetadataCandidate = _nativeImageMetadataReader.Read(filePath);
        var fileNameCandidate = _fileNameDateParser.Parse(Path.GetFileNameWithoutExtension(filePath));
        var fileSystemCandidate = _fileSystemDateReader.Read(filePath);

        var retained = nativeMetadataCandidate ?? fileNameCandidate ?? fileSystemCandidate;
        var retainedSource = retained is null
            ? ImageDateSource.None
            : retained == nativeMetadataCandidate
            ? ImageDateSource.NativeMetadata
            : retained == fileNameCandidate
                ? ImageDateSource.FileName
                : retained == fileSystemCandidate
                    ? ImageDateSource.FileSystem
                    : ImageDateSource.None;

        var normalizedFileNamePreview = retained is null
            ? null
            : NormalizedFileNameBuilder.Build(retained.Date, retained.HasTime, Path.GetExtension(filePath), filePath);

        return new ImageDateMetadata(
            nativeMetadataCandidate?.Date,
            fileNameCandidate?.Date,
            fileSystemCandidate?.Date,
            retained?.Date,
            retainedSource,
            retained?.HasTime ?? false,
            normalizedFileNamePreview);
    }
}
