using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed partial class ThemeExporter : IThemeExporter
{
    private static readonly string[] PackagingOnlyFiles = ["info", "author.txt", "title.txt", "note.txt"];

    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryMode = FileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public ThemePackageResult ExportPackage(string themePath, string outputDirectory, string system, string name, string? previewPath)
    {
        ValidateArguments(themePath, system, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var packagePath = Path.Combine(outputDirectory, $"{system.ToUpperInvariant()}.{name}.tar.gz");
        var temporaryPath = $"{packagePath}.{Guid.NewGuid():N}.tmp";
        var fileCount = 0;
        var includesPreview = previewPath is not null && File.Exists(previewPath);
        try
        {
            using (var output = File.Create(temporaryPath))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            using (var writer = new TarWriter(gzip, TarEntryFormat.Gnu))
            {
                writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, $"{name}/") { Mode = DirectoryMode });
                foreach (var directory in Directory.EnumerateDirectories(themePath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var relativePath = ToArchivePath(themePath, directory);
                    if (!UiTreeHash.IsIgnored(relativePath))
                    {
                        writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, $"{name}/{relativePath}/") { Mode = DirectoryMode });
                    }
                }

                foreach (var filePath in EnumeratePackagedFiles(themePath))
                {
                    WriteFile(writer, filePath, $"{name}/{ToArchivePath(themePath, filePath)}");
                    fileCount++;
                }

                if (includesPreview)
                {
                    WriteFile(writer, previewPath!, $"{name}.png");
                }
            }

            File.Move(temporaryPath, packagePath, true);
            return new ThemePackageResult(packagePath, fileCount, includesPreview);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string SendToPackaging(string themePath, string omThemesRootPath, string system, string name)
    {
        ValidateArguments(themePath, system, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(omThemesRootPath);
        if (!Directory.Exists(Path.Combine(omThemesRootPath, "new-packaging")))
        {
            throw new DirectoryNotFoundException($"{omThemesRootPath} is not an om_themes folder (new-packaging/ not found).");
        }

        var themesPath = Path.Combine(omThemesRootPath, "new-packaging", system, "themes");
        var targetPath = Path.Combine(themesPath, name);
        var stagingPath = Path.Combine(themesPath, $".{name}.mts-stage-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(themesPath, $".{name}.mts-backup-{Guid.NewGuid():N}");
        var targetMoved = false;
        Directory.CreateDirectory(themesPath);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(themePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(themePath, filePath);
                if (UiTreeHash.IsIgnored(relativePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(stagingPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath);
            }

            Directory.CreateDirectory(stagingPath);
            if (Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, backupPath);
                targetMoved = true;
            }

            Directory.Move(stagingPath, targetPath);
            if (targetMoved)
            {
                Directory.Delete(backupPath, true);
            }

            return targetPath;
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            if (targetMoved && !Directory.Exists(targetPath))
            {
                Directory.Move(backupPath, targetPath);
            }

            throw;
        }
    }

    private static IEnumerable<string> EnumeratePackagedFiles(string themePath)
    {
        return Directory.EnumerateFiles(themePath, "*", SearchOption.AllDirectories)
            .Where(filePath =>
            {
                var relativePath = ToArchivePath(themePath, filePath);
                return !UiTreeHash.IsIgnored(relativePath)
                    && !PackagingOnlyFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal);
    }

    private static void WriteFile(TarWriter writer, string filePath, string entryName)
    {
        using var content = File.OpenRead(filePath);
        writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, entryName)
        {
            Mode = FileMode,
            ModificationTime = File.GetLastWriteTimeUtc(filePath),
            DataStream = content,
        });
    }

    private static string ToArchivePath(string rootPath, string path)
    {
        return Path.GetRelativePath(rootPath, path).Replace('\\', '/');
    }

    private static void ValidateArguments(string themePath, string system, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        if (!Directory.Exists(themePath))
        {
            throw new DirectoryNotFoundException($"Theme directory not found: {themePath}");
        }

        if (!PackagingName().IsMatch(system) || !PackagingName().IsMatch(name))
        {
            throw new ArgumentException($"System and theme names must be lowercase letters, digits or underscores ('{system}', '{name}').");
        }
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex PackagingName();
}
