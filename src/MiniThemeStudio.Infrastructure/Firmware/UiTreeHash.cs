using System.Security.Cryptography;
using System.Text;

namespace MiniThemeStudio.Infrastructure.Firmware;

public sealed record UiTreeFingerprint(string Sha256, int FileCount);

public static class UiTreeHash
{
    public static bool IsIgnored(string relativePath)
    {
        return relativePath.Split('/', '\\').Any(segment => segment.StartsWith('.'));
    }

    public static UiTreeFingerprint ComputeForDirectory(string rootPath, CancellationToken cancellationToken = default)
    {
        var fileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            if (!IsIgnored(relativePath))
            {
                fileHashes[relativePath] = ComputeFileSha256(filePath);
            }
        }

        return Combine(fileHashes);
    }

    public static UiTreeFingerprint Combine(IReadOnlyDictionary<string, string> fileHashes)
    {
        var manifest = new StringBuilder();
        foreach (var (relativePath, sha256) in fileHashes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            manifest.Append(relativePath).Append('\t').Append(sha256).Append('\n');
        }

        var treeSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
        return new UiTreeFingerprint(treeSha256, fileHashes.Count);
    }

    public static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
