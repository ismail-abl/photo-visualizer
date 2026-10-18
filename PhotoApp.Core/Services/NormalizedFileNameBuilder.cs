using System.Security.Cryptography;
using System.Text;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class NormalizedFileNameBuilder
{
    public static string Build(ImageDateCandidate date, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        var extensionValue = NormalizeExtension(extension);
        var baseName = date.HasTime
            ? $"IMG-{date.WallClock:yyyyMMdd-HHmmss}"
            : $"IMG-{date.WallClock:yyyyMMdd}";

        if (!date.HasTime || !string.IsNullOrWhiteSpace(collisionToken))
        {
            var token = collisionToken ?? GenerateShortHash(sourceIdentifier);
            baseName += $"-{token}";
        }

        return baseName + extensionValue;
    }

    public static string? Build(ImageDateMetadata metadata, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        return metadata.RetainedCandidate is null
            ? null
            : Build(metadata.RetainedCandidate, extension, sourceIdentifier, collisionToken);
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
