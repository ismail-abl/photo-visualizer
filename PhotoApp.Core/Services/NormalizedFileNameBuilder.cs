using System.Security.Cryptography;
using System.Text;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class NormalizedFileNameBuilder
{
    public static string Build(DateTimeOffset date, bool hasTime, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        var extensionValue = NormalizeExtension(extension);
        var baseName = hasTime
            ? $"IMG-{date.DateTime:yyyyMMdd-HHmmss}"
            : $"IMG-{date.DateTime:yyyyMMdd}";

        if (!hasTime || !string.IsNullOrWhiteSpace(collisionToken))
        {
            var token = collisionToken ?? GenerateShortHash(sourceIdentifier);
            baseName += $"-{token}";
        }

        return baseName + extensionValue;
    }

    public static string? Build(ImageDateMetadata metadata, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        if (metadata.RetainedDate is null)
        {
            return null;
        }

        return Build(metadata.RetainedDate.Value, metadata.RetainedDateHasTime, extension, sourceIdentifier, collisionToken);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ext.ToLowerInvariant();
    }

    private static string GenerateShortHash(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 6).ToLowerInvariant();
    }
}
