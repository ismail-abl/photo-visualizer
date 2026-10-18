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
        var native = _nativeImageMetadataReader.Read(filePath);
        var fromName = _fileNameDateParser.Parse(Path.GetFileNameWithoutExtension(filePath));
        var fromFileSystem = _fileSystemDateReader.Read(filePath);

        var retained = native ?? fromName ?? fromFileSystem;
        var source = native is not null ? ImageDateSource.NativeMetadata
            : fromName is not null ? ImageDateSource.FileName
            : fromFileSystem is not null ? ImageDateSource.FileSystem
            : ImageDateSource.None;

        var preview = retained is null
            ? null
            : NormalizedFileNameBuilder.Build(retained, Path.GetExtension(filePath), filePath);

        return new ImageDateMetadata(native, fromName, fromFileSystem, retained, source, preview);
    }
}
