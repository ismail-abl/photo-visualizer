using ImageMagick;
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class MetadataReaderTests
{
    private static readonly MetadataReader Reader = new();

    [Fact]
    public void ExifOriginal_KeepsWallClock_SubSecondsAndOffset()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            exif.SetValue(ExifTag.SubsecTimeOriginal, "123");
            exif.SetValue(ExifTag.OffsetTimeOriginal, "+09:00");
        });

        var date = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.ExifOriginal);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15, 123), date.WallClock);
        Assert.Equal(DatePrecision.SubSecond, date.Precision);
        Assert.Equal(TimeSpan.FromHours(9), date.Offset);
    }

    [Fact]
    public void ExifWithoutOffset_IsFloating()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        var date = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.ExifOriginal);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), date.WallClock);
        Assert.Equal(DatePrecision.Second, date.Precision);
        Assert.Null(date.Offset);
        Assert.False(date.IsUtc);
    }

    [Fact]
    public void ExifDigitized_MakeModelAndSoftware_AreRead()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeDigitized, "2023:03:04 13:20:16");
            exif.SetValue(ExifTag.Make, "Xiaomi");
            exif.SetValue(ExifTag.Model, "M2012K11AG");
            exif.SetValue(ExifTag.Software, "MIUI");
        });

        var metadata = Reader.Read(path);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 16), metadata.Dates.Single(d => d.Origin == CandidateOrigin.ExifDigitized).WallClock);
        Assert.Equal("Xiaomi", metadata.Make);
        Assert.Equal("M2012K11AG", metadata.Model);
        Assert.Equal("MIUI", metadata.Software);
        Assert.Equal("jpg", metadata.DetectedExtension);
    }

    [Fact]
    public void Gps_IsUtc()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ExifTag.GPSTimestamp, [new Rational(12, 1), new Rational(20, 1), new Rational(14, 1)]);
        });

        var gps = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.Gps);

        Assert.True(gps.IsUtc);
        Assert.Equal(new DateTime(2023, 3, 4, 12, 20, 14), gps.WallClock);
    }

    [Fact]
    public void MalformedGpsTimestamp_OnlyCostsTheGpsSource()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            exif.SetValue(ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ExifTag.GPSTimestamp, [new Rational(10, 0), new Rational(0, 1), new Rational(0, 1)]);
        });

        var metadata = Reader.Read(path);

        Assert.Null(metadata.ReadError);
        Assert.Contains(metadata.Dates, d => d.Origin == CandidateOrigin.ExifOriginal && d.WallClock == new DateTime(2023, 3, 4, 13, 20, 15));
        Assert.DoesNotContain(metadata.Dates, d => d.Origin == CandidateOrigin.Gps);
        Assert.Contains(metadata.SourceWarnings, warning => warning.StartsWith("gps", StringComparison.Ordinal));
    }

    [Fact]
    public void WellFormedFile_HasNoSourceWarnings()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        Assert.Empty(Reader.Read(path).SourceWarnings);
    }

    [Fact]
    public void ExifWrittenWithAZone_IsTreatedAsUtc()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2024:01:05 15:04:12+09:00");

        var date = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.ExifOriginal);

        Assert.True(date.IsUtc);
        Assert.Equal(new DateTime(2024, 1, 5, 6, 4, 12), date.WallClock);
    }

    [Fact]
    public void XmpSidecar_IsRead()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("scan.png");
        var sidecar = temp.Combine("scan.xmp");
        TestImages.WritePng(path);
        File.WriteAllText(sidecar, """
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/" photoshop:DateCreated="2019-05-01T10:11:12+02:00"/>
            </rdf:RDF></x:xmpmeta>
            """);

        var date = Reader.Read(path, [sidecar]).Dates.Single(d => d.Origin == CandidateOrigin.XmpSidecar);

        Assert.Equal(new DateTime(2019, 5, 1, 10, 11, 12), date.WallClock);
        Assert.Equal(TimeSpan.FromHours(2), date.Offset);
    }

    [Fact]
    public void MalformedSidecar_IsIgnored()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("scan.png");
        var sidecar = temp.Combine("scan.xmp");
        TestImages.WritePng(path);
        File.WriteAllText(sidecar, "pas du XML");

        var metadata = Reader.Read(path, [sidecar]);

        Assert.Null(metadata.ReadError);
        Assert.DoesNotContain(metadata.Dates, d => d.Origin == CandidateOrigin.XmpSidecar);
    }

    [Fact]
    public void DetectedExtension_RevealsTheRealFormat()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WritePng(path);

        Assert.Equal("png", Reader.Read(path).DetectedExtension);
    }

    [Fact]
    public void AppleMov_GivesTheAppleDateWithItsOffset_AndTheUtcHeader()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("IMG_0477.MOV");
        TestVideos.Write(path, new DateTime(2024, 7, 11, 8, 7, 10), new Dictionary<string, string>
        {
            [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0900",
            [QuickTimeKeysReader.ContentIdentifierKey] = "ID-1",
            [QuickTimeKeysReader.MakeKey] = "Apple"
        });

        var metadata = Reader.Read(path);

        var apple = metadata.Dates.Single(d => d.Origin == CandidateOrigin.QuickTimeApple);
        var header = metadata.Dates.Single(d => d.Origin == CandidateOrigin.QuickTimeMovieHeader);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), apple.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), apple.Offset);
        Assert.True(header.IsUtc);
        Assert.Equal(new DateTime(2024, 7, 11, 8, 7, 10), header.WallClock);
        Assert.Equal("ID-1", metadata.ContentIdentifier);
        Assert.Equal("Apple", metadata.Make);
        Assert.Equal("mov", metadata.DetectedExtension);
    }

    [Fact]
    public void Mp4_GivesTheUtcHeaderOnly()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("20260917_222944.mp4");
        TestVideos.Write(path, new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");

        var metadata = Reader.Read(path);

        Assert.Equal(new DateTime(2026, 9, 17, 20, 29, 44), metadata.Dates.Single().WallClock);
        Assert.Equal(CandidateOrigin.QuickTimeMovieHeader, metadata.Dates.Single().Origin);
        Assert.Equal("mp4", metadata.DetectedExtension);
    }

    [Fact]
    public void UnreadableFile_ReportsAnError()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("IMG_20240101_101010.png");
        File.WriteAllBytes(path, [1, 2, 3]);

        var metadata = Reader.Read(path);

        Assert.NotNull(metadata.ReadError);
        Assert.Empty(metadata.Dates);
    }

    [Fact]
    public void PngWithoutMetadata_HasNoDates()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Empty(Reader.Read(path).Dates);
    }

    [Fact]
    public void Sha256ContentHasher_DependsOnTheContentOnly()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("a.bin"), [1, 2, 3]);
        File.WriteAllBytes(temp.Combine("b.bin"), [1, 2, 3]);
        File.WriteAllBytes(temp.Combine("c.bin"), [4, 5, 6]);
        var hasher = new Sha256ContentHasher();

        Assert.Equal(64, hasher.Hash(temp.Combine("a.bin")).Length);
        Assert.Equal(hasher.Hash(temp.Combine("a.bin")), hasher.Hash(temp.Combine("b.bin")));
        Assert.NotEqual(hasher.Hash(temp.Combine("a.bin")), hasher.Hash(temp.Combine("c.bin")));
    }
}
