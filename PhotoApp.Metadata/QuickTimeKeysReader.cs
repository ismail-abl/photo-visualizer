using System.Buffers.Binary;
using System.Text;

namespace PhotoApp.Metadata;

/// <summary>
/// Lit les clés Apple d'un MOV/MP4 (moov/meta : keys + ilst) telles qu'écrites.
/// MetadataExtractor convertit com.apple.quicktime.creationdate dans le fuseau du PC et perd le décalage d'origine.
/// </summary>
public static class QuickTimeKeysReader
{
    public const string CreationDateKey = "com.apple.quicktime.creationdate";
    public const string ContentIdentifierKey = "com.apple.quicktime.content.identifier";
    public const string MakeKey = "com.apple.quicktime.make";
    public const string ModelKey = "com.apple.quicktime.model";
    public const string SoftwareKey = "com.apple.quicktime.software";

    private const uint Utf8Type = 1;
    private const long MaxListBytes = 1 << 20;   // keys et ilst font quelques Ko en pratique

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static IReadOnlyDictionary<string, string> Read(Stream stream)
    {
        try
        {
            var moov = FindBox(stream, 0, stream.Length, "moov");
            var meta = moov is null ? null : FindBox(stream, moov.Value.Start, moov.Value.End, "meta");
            if (meta is null)
            {
                return Empty;
            }

            // QuickTime : « meta » contient directement ses boîtes ; ISO (MP4) : c'est une FullBox (4 octets de version et drapeaux).
            var children = meta.Value.Start;
            if (!IsBoxAt(stream, children, meta.Value.End))
            {
                children += 4;
            }

            var keys = FindBox(stream, children, meta.Value.End, "keys");
            var ilst = FindBox(stream, children, meta.Value.End, "ilst");
            if (keys is null || ilst is null)
            {
                return Empty;
            }

            return ReadValues(ReadBytes(stream, ilst.Value), ReadKeyNames(ReadBytes(stream, keys.Value)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return Empty;
        }
    }

    /// <summary>Contenu d'une boîte, en-tête exclu : [Start, End[.</summary>
    private readonly record struct Box(long Start, long End);

    private static Box? FindBox(Stream stream, long from, long to, string type)
    {
        var header = new byte[16];
        var position = from;
        while (position + 8 <= to)
        {
            stream.Position = position;
            stream.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var name = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8L;

            if (size == 1)
            {
                stream.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = to - position;   // jusqu'à la fin du parent
            }

            if (size < headerSize || position + size > to)
            {
                return null;
            }

            if (name == type)
            {
                return new Box(position + headerSize, position + size);
            }

            position += size;
        }

        return null;
    }

    // Une boîte plausible commence ici : taille cohérente et type en lettres ASCII.
    private static bool IsBoxAt(Stream stream, long position, long end)
    {
        if (position + 8 > end)
        {
            return false;
        }

        var header = new byte[8];
        stream.Position = position;
        stream.ReadExactly(header, 0, 8);
        var size = BinaryPrimitives.ReadUInt32BigEndian(header);
        return size >= 8 && position + size <= end && header.AsSpan(4, 4).ToArray().All(b => b is >= 0x20 and < 0x7F);
    }

    private static byte[] ReadBytes(Stream stream, Box box)
    {
        var length = box.End - box.Start;
        if (length > MaxListBytes)
        {
            throw new InvalidDataException("Liste de métadonnées QuickTime trop grande.");
        }

        var bytes = new byte[length];
        stream.Position = box.Start;
        stream.ReadExactly(bytes);
        return bytes;
    }

    // keys : version et drapeaux (4), nombre de clés (4), puis pour chaque clé : taille (4), espace de noms (4), nom.
    private static List<string> ReadKeyNames(byte[] bytes)
    {
        var names = new List<string>();
        if (bytes.Length < 8)
        {
            return names;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        var position = 8;
        for (var i = 0; i < count && position + 8 <= bytes.Length; i++)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            if (size < 8 || position + size > bytes.Length)
            {
                break;
            }

            names.Add(Encoding.UTF8.GetString(bytes, position + 8, size - 8));
            position += size;
        }

        return names;
    }

    // ilst : une boîte par valeur, dont le type est l'index de la clé (à partir de 1), contenant une boîte « data » :
    // type (4, 1 = UTF-8), locale (4), valeur.
    private static Dictionary<string, string> ReadValues(byte[] bytes, List<string> names)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position + 8 <= bytes.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            if (size < 8 || position + size > bytes.Length)
            {
                break;
            }

            var index = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 4));
            var data = position + 8;
            if (index >= 1 && index <= names.Count && data + 16 <= position + size
                && Encoding.ASCII.GetString(bytes, data + 4, 4) == "data")
            {
                var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data));
                var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data + 8)) & 0x00FFFFFF;
                if (type == Utf8Type && dataSize >= 16 && data + dataSize <= position + size)
                {
                    values[names[index - 1]] = Encoding.UTF8.GetString(bytes, data + 16, dataSize - 16);
                }
            }

            position += size;
        }

        return values;
    }
}
