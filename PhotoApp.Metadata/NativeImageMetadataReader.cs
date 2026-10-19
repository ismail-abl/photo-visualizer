using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Metadata;

public class NativeImageMetadataReader
{
    // OffsetTimeOriginal / OffsetTimeDigitized (EXIF 2.31), lus en texte : "+09:00".
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagOffsetTimeDigitized = 0x9012;

    // Dates de prise de vue uniquement : xmp:ModifyDate et xmp:MetadataDate sont des dates d'édition.
    private static readonly string[] XmpDateKeys = ["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"];

    public virtual DateCandidate? Read(string filePath)
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

        return ReadExifDate(directories) ?? ReadXmpDate(directories);
    }

    private static DateCandidate? ReadExifDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var exifDirectory in directories.OfType<ExifSubIfdDirectory>())
        {
            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
            {
                return new DateCandidate(original, DatePrecision.Second, CandidateOrigin.ExifOriginal, ReadOffset(exifDirectory, TagOffsetTimeOriginal));
            }

            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
            {
                return new DateCandidate(digitized, DatePrecision.Second, CandidateOrigin.ExifDigitized, ReadOffset(exifDirectory, TagOffsetTimeDigitized));
            }
        }

        return null;
    }

    private static TimeSpan? ReadOffset(ExifSubIfdDirectory directory, int tag) =>
        UtcOffsetParser.TryParse(directory.GetString(tag), out var offset) ? offset : null;

    private static DateCandidate? ReadXmpDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var xmpDirectory in directories.OfType<XmpDirectory>())
        {
            var properties = xmpDirectory.GetXmpProperties();
            foreach (var key in XmpDateKeys)
            {
                if (properties.TryGetValue(key, out var value) && XmpDateParser.Parse(value) is { } candidate)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
