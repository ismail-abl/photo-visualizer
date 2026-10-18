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

        var (retained, source) = DateSelector.Select(native, fromName, fromFileSystem, DateTime.UtcNow);

        var preview = retained is null || !DateSelector.IsReliable(source)
            ? null
            : NormalizedFileNameBuilder.Preview(retained);

        return new ImageDateMetadata(native, fromName, fromFileSystem, retained, source, preview);
    }
}
