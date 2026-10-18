namespace PhotoApp.Tests;

public sealed class NativeImageMetadataReaderTests
{
    [Fact]
    public void Read_ExifLocalTime_IsKeptAsWallClock_NotShiftedToUtc()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        var result = new NativeImageMetadataReader().Read(path);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Null(result.Offset);
        Assert.True(result.HasTime);
    }

    [Fact]
    public void Read_ExifOffsetTime_IsReturned()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15", "+09:00");

        var result = new NativeImageMetadataReader().Read(path);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
    }

    [Fact]
    public void Read_ImageWithoutMetadata_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Null(new NativeImageMetadataReader().Read(path));
    }
}
