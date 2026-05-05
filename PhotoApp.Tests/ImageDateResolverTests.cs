using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class ImageDateResolverTests
{
    [Fact]
    public void Resolve_PrefersNativeMetadata_OverFilenameAndFilesystem()
    {
        var nativeReader = new StubNativeImageMetadataReader(new ImageDateCandidate(new DateTimeOffset(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)), true));
        var fileNameParser = new StubFileNameDateParser(new ImageDateCandidate(new DateTimeOffset(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc)), true));
        var fileSystemReader = new StubFileSystemDateReader(new ImageDateCandidate(new DateTimeOffset(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)), true));
        var resolver = new ImageDateResolver(nativeReader, fileNameParser, fileSystemReader);

        var result = resolver.Resolve("photo.jpg");

        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.OriginalDate?.UtcDateTime);
        Assert.Equal(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc), result.CreateDate?.UtcDateTime);
        Assert.Equal(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), result.ModifyDate?.UtcDateTime);
        Assert.Equal(ImageDateSource.NativeMetadata, result.RetainedSource);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.RetainedDate?.UtcDateTime);
        Assert.True(result.RetainedDateHasTime);
        Assert.Equal("IMG-20240102-030405.jpg", result.NormalizedFileNamePreview);
    }

    [Fact]
    public void Resolve_FallsBackToFilename_WhenNativeMetadataIsMissing()
    {
        var nativeReader = new StubNativeImageMetadataReader(null);
        var fileNameParser = new StubFileNameDateParser(new ImageDateCandidate(new DateTimeOffset(new DateTime(2021, 10, 29, 0, 0, 0, DateTimeKind.Utc)), false));
        var fileSystemReader = new StubFileSystemDateReader(new ImageDateCandidate(new DateTimeOffset(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)), true));
        var resolver = new ImageDateResolver(nativeReader, fileNameParser, fileSystemReader);

        var result = resolver.Resolve("IMG-20211029-WA0039.jpg");

        Assert.Null(result.OriginalDate);
        Assert.Equal(new DateTime(2021, 10, 29, 0, 0, 0, DateTimeKind.Utc), result.CreateDate?.UtcDateTime);
        Assert.Equal(ImageDateSource.FileName, result.RetainedSource);
        Assert.False(result.RetainedDateHasTime);
        Assert.Equal("IMG-20211029-58ebe3.jpg", result.NormalizedFileNamePreview);
    }

    [Fact]
    public void Resolve_FallsBackToFilesystem_WhenEarlierSourcesAreMissing()
    {
        var nativeReader = new StubNativeImageMetadataReader(null);
        var fileNameParser = new StubFileNameDateParser(null);
        var fileSystemReader = new StubFileSystemDateReader(new ImageDateCandidate(new DateTimeOffset(new DateTime(2026, 5, 4, 18, 19, 20, DateTimeKind.Utc)), true));
        var resolver = new ImageDateResolver(nativeReader, fileNameParser, fileSystemReader);

        var result = resolver.Resolve("photo.jpg");

        Assert.Null(result.OriginalDate);
        Assert.Null(result.CreateDate);
        Assert.Equal(new DateTime(2026, 5, 4, 18, 19, 20, DateTimeKind.Utc), result.ModifyDate?.UtcDateTime);
        Assert.Equal(ImageDateSource.FileSystem, result.RetainedSource);
        Assert.True(result.RetainedDateHasTime);
        Assert.Equal("IMG-20260504-181920.jpg", result.NormalizedFileNamePreview);
    }

    private sealed class StubNativeImageMetadataReader : NativeImageMetadataReader
    {
        private readonly ImageDateCandidate? _result;

        public StubNativeImageMetadataReader(ImageDateCandidate? result)
        {
            _result = result;
        }

        public override ImageDateCandidate? Read(string filePath)
        {
            return _result;
        }
    }

    private sealed class StubFileNameDateParser : FileNameDateParser
    {
        private readonly ImageDateCandidate? _result;

        public StubFileNameDateParser(ImageDateCandidate? result)
        {
            _result = result;
        }

        public override ImageDateCandidate? Parse(string fileName)
        {
            return _result;
        }
    }

    private sealed class StubFileSystemDateReader : FileSystemDateReader
    {
        private readonly ImageDateCandidate? _result;

        public StubFileSystemDateReader(ImageDateCandidate? result)
        {
            _result = result;
        }

        public override ImageDateCandidate? Read(string filePath)
        {
            return _result;
        }
    }
}
