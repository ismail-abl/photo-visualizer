using System.Buffers.Binary;
using System.Text;

namespace PhotoApp.Tests;

/// <summary>
/// Écrit de petits conteneurs MOV/MP4 que MetadataExtractor sait lire : ftyp, mdat facultatif, puis moov avec mvhd
/// et, en option, les clés Apple (meta : hdlr + keys + ilst). Aucune donnée privée.
/// </summary>
public static class TestVideos
{
    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Write(
        string path,
        DateTime movieHeaderUtc,
        IReadOnlyDictionary<string, string>? appleKeys = null,
        string brand = "qt  ",
        bool isoMeta = false,
        int mediaDataBytes = 0)
    {
        var ftyp = Box("ftyp", Ascii(brand), U32(0), Ascii(brand));
        var mdat = mediaDataBytes > 0 ? Box("mdat", new byte[mediaDataBytes]) : [];
        var moovChildren = new List<byte[]> { MovieHeader(movieHeaderUtc) };
        if (appleKeys is { Count: > 0 })
        {
            moovChildren.Add(AppleMeta(appleKeys, isoMeta));
        }

        File.WriteAllBytes(path, [.. ftyp, .. mdat, .. Box("moov", [.. moovChildren])]);
    }

    private static byte[] MovieHeader(DateTime utc)
    {
        var seconds = (uint)(DateTime.SpecifyKind(utc, DateTimeKind.Utc) - QuickTimeEpoch).TotalSeconds;
        var matrix = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(16), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(32), 0x40000000);
        // version/drapeaux, création, modification, échelle, durée, vitesse, volume, réservé, matrice, prévisualisation…, piste suivante
        return Box("mvhd", U32(0), U32(seconds), U32(seconds), U32(600), U32(0), U32(0x00010000), U16(0x0100), new byte[10], matrix, new byte[24], U32(2));
    }

    private static byte[] AppleMeta(IReadOnlyDictionary<string, string> keys, bool isoMeta)
    {
        var items = keys.ToList();
        var hdlr = Box("hdlr", U32(0), U32(0), Ascii("mdta"), new byte[12], new byte[1]);
        var keyEntries = items.Select(item =>
        {
            var name = Encoding.UTF8.GetBytes(item.Key);
            byte[] entry = [.. U32((uint)(8 + name.Length)), .. Ascii("mdta"), .. name];
            return entry;
        });
        var keysBox = Box("keys", [U32(0), U32((uint)items.Count), .. keyEntries]);
        var entries = items.Select((item, index) =>
            Box("\0\0\0" + (char)(index + 1), Box("data", U32(1), U32(0), Encoding.UTF8.GetBytes(item.Value))));
        var ilst = Box("ilst", [.. entries]);

        // QuickTime : « meta » contient directement ses boîtes ; ISO (MP4) : 4 octets de version et drapeaux d'abord.
        return isoMeta ? Box("meta", U32(0), hdlr, keysBox, ilst) : Box("meta", hdlr, keysBox, ilst);
    }

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = content.SelectMany(part => part).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
