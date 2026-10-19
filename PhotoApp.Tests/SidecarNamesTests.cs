using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SidecarNamesTests
{
    [Theory]
    [InlineData("holiday.jpg", "IMG-20230304-132015.jpg", "holiday.xmp", "IMG-20230304-132015.xmp")]
    [InlineData("holiday.jpg", "IMG-20230304-132015.jpg", "holiday.jpg.xmp", "IMG-20230304-132015.jpg.xmp")]
    [InlineData("IMG_0477.HEIC", "IMG-20240105-150412.heic", "IMG_0477.AAE", "IMG-20240105-150412.AAE")]
    [InlineData("a.jpg", "IMG-1.jpg", "A.XMP", "IMG-1.XMP")]
    public void Rename_FollowsTheConventionOfTheOriginalSidecar(string oldMedia, string newMedia, string sidecar, string expected)
    {
        Assert.Equal(expected, SidecarNames.Rename(oldMedia, newMedia, sidecar));
    }

    [Fact]
    public void Rename_UnrelatedSidecar_ReturnsNull()
    {
        Assert.Null(SidecarNames.Rename("holiday.jpg", "IMG-20230304-132015.jpg", "other.xmp"));
    }
}
