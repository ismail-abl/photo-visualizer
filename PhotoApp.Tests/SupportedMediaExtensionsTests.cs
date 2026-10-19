using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class SupportedMediaExtensionsTests
{
    [Theory]
    [InlineData(".JPG", true, false)]
    [InlineData(".heic", true, false)]
    [InlineData(".gif", true, false)]
    [InlineData(".MOV", false, true)]
    [InlineData(".mp4", false, true)]
    [InlineData(".xmp", false, false)]
    [InlineData(".txt", false, false)]
    [InlineData(null, false, false)]
    public void Classifies(string? extension, bool photo, bool video)
    {
        Assert.Equal(photo, SupportedMediaExtensions.IsPhoto(extension));
        Assert.Equal(video, SupportedMediaExtensions.IsVideo(extension));
        Assert.Equal(photo || video, SupportedMediaExtensions.IsSupported(extension));
    }

    [Theory]
    [InlineData(".xmp")]
    [InlineData(".AAE")]
    public void Sidecars(string extension)
    {
        Assert.True(SupportedMediaExtensions.IsSidecar(extension));
    }

    [Theory]
    [InlineData(".jpg", ".JPEG", true)]
    [InlineData(".heif", ".heic", true)]
    [InlineData(".tif", ".tiff", true)]
    [InlineData(".m4v", ".mp4", true)]
    [InlineData(".jpg", ".png", false)]
    [InlineData(".mov", ".mp4", false)]
    public void SameFormat(string first, string second, bool expected)
    {
        Assert.Equal(expected, SupportedMediaExtensions.SameFormat(first, second));
    }
}
