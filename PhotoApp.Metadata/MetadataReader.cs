using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Metadata;

/// <summary>
/// Lit les métadonnées brutes d'un média (EXIF, GPS, XMP et sidecar .xmp, QuickTime, vrai format) sans rien décider :
/// le choix de la date appartient au moteur de PhotoApp.Core.
/// </summary>
public sealed class MetadataReader
{
    // OffsetTimeOriginal / OffsetTimeDigitized (EXIF 2.31), lus en texte : "+09:00".
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagOffsetTimeDigitized = 0x9012;

    // Dates de prise de vue uniquement : xmp:ModifyDate et xmp:MetadataDate sont des dates d'édition.
    private static readonly string[] XmpDateKeys = ["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"];

    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1);

    public RawMetadata Read(string path, IReadOnlyList<string>? sidecars = null)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception ex)
        {
            // Format inconnu, fichier tronqué ou verrouillé : la décision sera « lecture impossible ».
            return new RawMetadata { ReadError = ex.Message };
        }

        var detected = Clean(directories.OfType<FileTypeDirectory>().FirstOrDefault()?.GetString(FileTypeDirectory.TagExpectedFileNameExtension))
            ?.ToLowerInvariant();
        IReadOnlyDictionary<string, string> apple = detected is "mov" or "mp4"
            ? QuickTimeKeysReader.Read(path)
            : new Dictionary<string, string>();

        var dates = new List<DateCandidate>();
        var exif = directories.OfType<ExifSubIfdDirectory>().ToList();
        AddExifDate(exif, ExifDirectoryBase.TagDateTimeOriginal, TagOffsetTimeOriginal, ExifDirectoryBase.TagSubsecondTimeOriginal, CandidateOrigin.ExifOriginal, dates);
        AddExifDate(exif, ExifDirectoryBase.TagDateTimeDigitized, TagOffsetTimeDigitized, ExifDirectoryBase.TagSubsecondTimeDigitized, CandidateOrigin.ExifDigitized, dates);
        AddGps(directories, dates);
        AddXmp(directories, dates);
        AddQuickTime(directories, apple, dates);
        foreach (var sidecar in sidecars ?? [])
        {
            if (string.Equals(Path.GetExtension(sidecar), ".xmp", StringComparison.OrdinalIgnoreCase))
            {
                AddXmpSidecar(sidecar, dates);
            }
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        return new RawMetadata
        {
            Dates = dates,
            Make = Clean(ifd0?.GetString(ExifDirectoryBase.TagMake)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.MakeKey)),
            Model = Clean(ifd0?.GetString(ExifDirectoryBase.TagModel)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.ModelKey)),
            Software = Clean(ifd0?.GetString(ExifDirectoryBase.TagSoftware)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.SoftwareKey)),
            ContentIdentifier = Clean(directories.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleMakernoteDirectory.TagContentIdentifier))
                                ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.ContentIdentifierKey)),
            DetectedExtension = detected
        };
    }

    private static void AddExifDate(
        List<ExifSubIfdDirectory> directories, int dateTag, int offsetTag, int subSecondTag, CandidateOrigin origin, List<DateCandidate> dates)
    {
        foreach (var directory in directories)
        {
            if (!directory.TryGetDateTime(dateTag, out var value))
            {
                continue;
            }

            // MetadataExtractor ne reporte pas les sous-secondes dans la date : elles ont leur propre balise.
            var wallClock = new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);
            var precision = DatePrecision.Second;
            if (SubSeconds.ToMilliseconds(directory.GetString(subSecondTag)?.Trim()) is { } milliseconds)
            {
                wallClock = wallClock.AddMilliseconds(milliseconds);
                precision = DatePrecision.SubSecond;
            }

            TimeSpan? offset = UtcOffsetParser.TryParse(directory.GetString(offsetTag), out var parsed) ? parsed : null;
            dates.Add(new DateCandidate(wallClock, precision, origin, offset));
            return;
        }
    }

    private static void AddGps(IEnumerable<MetadataExtractor.Directory> directories, List<DateCandidate> dates)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is not null && gps.TryGetGpsDate(out var utc))
        {
            dates.Add(new DateCandidate(utc, DatePrecision.Second, CandidateOrigin.Gps, isUtc: true));
        }
    }

    private static void AddXmp(IEnumerable<MetadataExtractor.Directory> directories, List<DateCandidate> dates)
    {
        foreach (var xmp in directories.OfType<XmpDirectory>())
        {
            if (FirstXmpDate(xmp, CandidateOrigin.Xmp) is { } date)
            {
                dates.Add(date);
                return;
            }
        }
    }

    private static void AddXmpSidecar(string path, List<DateCandidate> dates)
    {
        try
        {
            var xmp = new XmpReader().Extract(File.ReadAllBytes(path));
            if (FirstXmpDate(xmp, CandidateOrigin.XmpSidecar) is { } date)
            {
                dates.Add(date);
            }
        }
        catch (Exception)
        {
            // Sidecar illisible ou mal formé : ignoré, les autres sources restent valables (spec de phase 1, §11).
        }
    }

    private static DateCandidate? FirstXmpDate(XmpDirectory xmp, CandidateOrigin origin)
    {
        var properties = xmp.GetXmpProperties();
        foreach (var key in XmpDateKeys)
        {
            if (properties.TryGetValue(key, out var value) && XmpDateParser.Parse(value, origin) is { } date)
            {
                return date;
            }
        }

        return null;
    }

    private static void AddQuickTime(
        IEnumerable<MetadataExtractor.Directory> directories, IReadOnlyDictionary<string, string> apple, List<DateCandidate> dates)
    {
        if (apple.TryGetValue(QuickTimeKeysReader.CreationDateKey, out var creation)
            && XmpDateParser.Parse(creation, CandidateOrigin.QuickTimeApple) is { } appleDate)
        {
            dates.Add(appleDate);
        }

        // mvhd : secondes depuis 1904, en UTC ; 0 (non renseigné) donne 1904-01-01.
        var header = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (header is not null && header.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created) && created > QuickTimeEpoch)
        {
            dates.Add(new DateCandidate(created, DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true));
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim().TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
