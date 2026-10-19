using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class NameAllocatorTests
{
    private static DateCandidate Shot() => new(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second);

    private static DateCandidate DayOnly() => new(new DateTime(2024, 1, 5), DatePrecision.Day);

    [Fact]
    public void Allocate_FirstFileOfASecond_HasNoSuffix()
    {
        Assert.Equal("IMG-20240105-150412.jpg", new NameAllocator().Allocate(Shot(), ".jpg"));
    }

    [Fact]
    public void Allocate_SameSecond_GetsIncreasingSuffixes()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-150412.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-02.jpg", allocator.Allocate(Shot(), ".jpg"));
    }

    [Fact]
    public void Allocate_DayOnly_AlwaysHasASuffixStartingAtOne()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-000000-01.jpg", allocator.Allocate(DayOnly(), ".jpg"));
        Assert.Equal("IMG-20240105-000000-02.jpg", allocator.Allocate(DayOnly(), ".jpg"));
    }

    [Fact]
    public void Allocate_NeverReusesAlreadyTakenNames_CaseInsensitive()
    {
        var allocator = new NameAllocator(["img-20240105-150412.JPG"]);

        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
    }

    [Theory]
    [InlineData(".JPEG", ".jpg")]
    [InlineData("jpg", ".jpg")]
    [InlineData(".PNG", ".png")]
    public void NormalizeExtension_LowercasesAndMapsJpeg(string input, string expected)
    {
        Assert.Equal(expected, NormalizedFileNameBuilder.NormalizeExtension(input));
    }

    [Fact]
    public void Allocate_VideoPrefix()
    {
        Assert.Equal("VID-20240105-150412.mp4", new NameAllocator().Allocate(Shot(), ".mp4", "VID"));
    }

    [Fact]
    public void Allocate_SameSecondWithAnotherExtension_StillGetsASuffix()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-150412.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-01.heic", allocator.Allocate(Shot(), ".heic"));
    }

    [Fact]
    public void TryReserve_TakesTheBaseName_Once()
    {
        var allocator = new NameAllocator();

        Assert.True(allocator.TryReserve("IMG-20240105-150412.heic"));
        Assert.False(allocator.TryReserve("img-20240105-150412.jpg"));
        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
    }
}
