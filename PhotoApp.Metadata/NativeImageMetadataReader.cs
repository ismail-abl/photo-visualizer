using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
using PhotoApp.Core.Models;

namespace PhotoApp.Metadata;

public class NativeImageMetadataReader
{
    public virtual ImageDateCandidate? Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (Exception)
        {
            return null;
        }

        var exifDate = ReadExifDate(directories);
        if (exifDate is not null)
        {
            return exifDate;
        }

        var xmpDate = ReadXmpDate(directories);
        if (xmpDate is not null)
        {
            return xmpDate;
        }

        return null;
    }

    private static ImageDateCandidate? ReadExifDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var directory in directories)
        {
            if (directory is not ExifSubIfdDirectory exifDirectory)
            {
                continue;
            }

            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var originalDateTime))
            {
                return new ImageDateCandidate(ToUtcOffset(originalDateTime), true);
            }

            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitizedDateTime))
            {
                return new ImageDateCandidate(ToUtcOffset(digitizedDateTime), true);
            }
        }

        return null;
    }

    private static ImageDateCandidate? ReadXmpDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var directory in directories)
        {
            if (directory is not XmpDirectory xmpDirectory)
            {
                continue;
            }

            var properties = xmpDirectory.GetXmpProperties();
            if (TryReadXmpProperty(properties, "xmp:CreateDate", out var xmpDate) ||
                TryReadXmpProperty(properties, "xmp:ModifyDate", out xmpDate) ||
                TryReadXmpProperty(properties, "xmp:MetadataDate", out xmpDate) ||
                TryReadXmpProperty(properties, "photoshop:DateCreated", out xmpDate))
            {
                return new ImageDateCandidate(xmpDate, true);
            }
        }

        return null;
    }

    private static bool TryReadXmpProperty(IDictionary<string, string> properties, string key, out DateTimeOffset dateTime)
    {
        if (properties.TryGetValue(key, out var value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dateTime))
        {
            return true;
        }

        dateTime = default;
        return false;
    }

    private static DateTimeOffset ToUtcOffset(DateTime dateTime)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }
}
