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

    public ThemePackageResult ExportPackage(string themePath, string outputDirectory, string system, string name, string? previewPath, string? baseUiPath)
    {
        ValidateArguments(themePath, system, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var packagePath = Path.Combine(outputDirectory, $"{system.ToUpperInvariant()}.{name}.tar.gz");
        var temporaryPath = $"{packagePath}.{Guid.NewGuid():N}.tmp";
        var includesPreview = previewPath is not null && File.Exists(previewPath);
        var (files, skipped) = SelectFiles(themePath, baseUiPath);
        var needsOverlay = baseUiPath is not null
            && !files.Contains("needs_overlay")
            && files.Any(relativePath => !File.Exists(Path.Combine(baseUiPath, relativePath)) && relativePath.StartsWith("resources/", StringComparison.Ordinal));
        try
        {
            using (var output = File.Create(temporaryPath))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            using (var writer = new TarWriter(gzip, TarEntryFormat.Gnu))
            {
                writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, $"{name}/") { Mode = DirectoryMode });
                foreach (var directory in ParentDirectories(files))
                {
                    writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, $"{name}/{directory}/") { Mode = DirectoryMode });
                }

                foreach (var relativePath in files)
                {
                    WriteFile(writer, Path.Combine(themePath, relativePath), $"{name}/{relativePath}");
                }

                if (needsOverlay)
                {
                    writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, $"{name}/needs_overlay") { Mode = FileMode, DataStream = new MemoryStream() });
                }

                if (includesPreview)
                {
                    WriteFile(writer, previewPath!, $"{name}.png");
                }
            }

            File.Move(temporaryPath, packagePath, true);
            return new ThemePackageResult(packagePath, files.Count, includesPreview, skipped);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static (List<string> Files, int Skipped) SelectFiles(string themePath, string? baseUiPath)
    {
        var files = new List<string>();
        var skipped = 0;
        foreach (var filePath in Directory.EnumerateFiles(themePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = ToArchivePath(themePath, filePath);
            if (UiTreeHash.IsIgnored(relativePath)
                || PackagingOnlyFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (baseUiPath is not null && IsUnchanged(filePath, Path.Combine(baseUiPath, relativePath)))
            {
                skipped++;
                continue;
            }

            files.Add(relativePath);
        }

        files.Sort(StringComparer.Ordinal);
        return (files, skipped);
    }

    private static bool IsUnchanged(string themeFile, string baseFile)
    {
        if (!File.Exists(baseFile) || new FileInfo(themeFile).Length != new FileInfo(baseFile).Length)
        {
            return false;
        }

        return File.ReadAllBytes(themeFile).AsSpan().SequenceEqual(File.ReadAllBytes(baseFile));
    }

    private static SortedSet<string> ParentDirectories(IEnumerable<string> files)
    {
        var directories = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var separator = file.LastIndexOf('/');
            while (separator > 0)
            {
                directories.Add(file[..separator]);
                separator = file.LastIndexOf('/', separator - 1);
            }
        }

        return directories;
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
