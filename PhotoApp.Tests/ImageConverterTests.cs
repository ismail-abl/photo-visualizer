using ImageMagick;
using PhotoApp.Core.Models;
using PhotoApp.Metadata;

namespace PhotoApp.Tests;

public sealed class ImageConverterTests
{
    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".JPEG", true)]
    [InlineData(".png", true)]
    [InlineData(".webp", true)]
    [InlineData(".tif", true)]
    [InlineData(".heic", true)]
    [InlineData(".heif", true)]
    [InlineData(".avif", true)]
    [InlineData(".gif", false)]   // animation perdue : non converti
    [InlineData(".txt", false)]
    [InlineData(null, false)]
    public void CanConvert_ReflectsWhatConvertToJpegSupports(string? extension, bool expected)
    {
        Assert.Equal(expected, ImageConverter.CanConvert(extension));
    }

    [Theory]
    [InlineData("in.png", MagickFormat.Png)]
    [InlineData("in.webp", MagickFormat.WebP)]
    [InlineData("in.tif", MagickFormat.Tiff)]
    [InlineData("in.avif", MagickFormat.Avif)]   // même décodeur (libheif) que HEIC, que Magick.NET ne sait pas écrire
    public void ConvertToJpeg_ProducesAReadableJpeg(string inputName, MagickFormat inputFormat)
    {
        using var temp = new TempDirectory();
        var input = temp.Combine(inputName);
        var output = temp.Combine("out.jpg");
        TestImages.Write(input, inputFormat);

        new ImageConverter().ConvertToJpeg(input, output, 90);

        var info = new MagickImageInfo(output);
        Assert.Equal(MagickFormat.Jpeg, info.Format);
        Assert.Equal(8u, info.Width);
    }

    [Fact]
    public void ConvertToJpeg_KeepsTheExifDate()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.webp");
        var output = temp.Combine("out.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 8, 8))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.WebP);
        }

        new ImageConverter().ConvertToJpeg(input, output, 90);

        var date = new MetadataReader().Read(output).Dates
            .FirstOrDefault(candidate => candidate.Origin == CandidateOrigin.ExifOriginal);
        Assert.NotNull(date);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), date.WallClock);
    }

    [Fact]
    public void ConvertToJpeg_TransparentPng_GetsAWhiteBackground()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.png");
        var output = temp.Combine("out.jpg");
        using (var image = new MagickImage(MagickColors.Transparent, 8, 8))
        {
            image.Write(input, MagickFormat.Png);
        }

        new ImageConverter().ConvertToJpeg(input, output, 90);

        using var result = new MagickImage(output);
        var pixel = result.GetPixels().GetPixel(4, 4).ToColor()!;
        Assert.True(pixel.R > 240 && pixel.G > 240 && pixel.B > 240, $"pixel attendu blanc, obtenu {pixel}");
    }

    [Fact]
    public void ConvertToJpeg_JpegSource_IsCopiedByteForByte()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.jpg");
        var output = temp.Combine("out.jpg");
        TestImages.WriteJpeg(input, "2023:03:04 13:20:15");

        new ImageConverter().ConvertToJpeg(input, output, 50);

        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
    }

    [Fact]
    public void ConvertToJpeg_UnsupportedExtension_ThrowsNotSupported()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.gif");
        File.WriteAllBytes(input, [1, 2, 3]);

        Assert.Throws<NotSupportedException>(() => new ImageConverter().ConvertToJpeg(input, temp.Combine("out.jpg"), 90));
    }
}
