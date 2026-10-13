using System.IO.Compression;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeExporter : IThemeExporter
{
    public int ExportZip(string themePath, string zipPath, string rootFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolderName);
        if (!Directory.Exists(themePath))
        {
            throw new DirectoryNotFoundException($"Theme directory not found: {themePath}");
        }

        var temporaryPath = $"{zipPath}.{Guid.NewGuid():N}.tmp";
        var count = 0;
        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                foreach (var filePath in Directory.EnumerateFiles(themePath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var relativePath = Path.GetRelativePath(themePath, filePath).Replace('\\', '/');
                    if (UiTreeHash.IsIgnored(relativePath))
                    {
                        continue;
                    }

                    archive.CreateEntryFromFile(filePath, $"{rootFolderName}/{relativePath}", CompressionLevel.Optimal);
                    count++;
                }
            }

            File.Move(temporaryPath, zipPath, true);
            return count;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
