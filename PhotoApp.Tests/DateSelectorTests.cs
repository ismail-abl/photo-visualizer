using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class DateSelectorTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    private static DateCandidate At(int year, int month, int day, bool hasTime = true) =>
        new(new DateTime(year, month, day, 10, 0, 0), hasTime ? DatePrecision.Second : DatePrecision.Day);

    [Fact]
    public void Select_PrefersNative_ThenFileName_ThenFileSystem()
    {
        var native = At(2024, 1, 2);
        var name = At(2025, 2, 3);
        var fs = At(2026, 3, 4);

        Assert.Equal((native, ImageDateSource.NativeMetadata), DateSelector.Select(native, name, fs, Now));
        Assert.Equal((name, ImageDateSource.FileName), DateSelector.Select(null, name, fs, Now));
        Assert.Equal((fs, ImageDateSource.FileSystem), DateSelector.Select(null, null, fs, Now));
        Assert.Equal((null, ImageDateSource.None), DateSelector.Select(null, null, null, Now));
    }

    [Fact]
    public void Select_SkipsImplausibleCandidates()
    {
        var resetClock = new DateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), DatePrecision.Second);
        var name = At(2025, 2, 3);

        var (candidate, source) = DateSelector.Select(resetClock, name, null, Now);

        Assert.Same(name, candidate);
        Assert.Equal(ImageDateSource.FileName, source);
    }

    [Theory]
    [InlineData(ImageDateSource.NativeMetadata, true)]
    [InlineData(ImageDateSource.FileName, true)]
    [InlineData(ImageDateSource.FileSystem, false)]
    [InlineData(ImageDateSource.None, false)]
    public void IsReliable_ExcludesTheFileSystemDate(ImageDateSource source, bool expected)
    {
        Assert.Equal(expected, DateSelector.IsReliable(source));
    }
}
