using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed partial class ThemeExporter(MiniThemeStudio.Core.Audio.IFFmpegAudioConverter? ffmpegAudioConverter = null) : IThemeExporter
{
    private readonly MiniThemeStudio.Core.Audio.IFFmpegAudioConverter _ffmpegAudioConverter = ffmpegAudioConverter ?? new MiniThemeStudio.Infrastructure.Audio.FFmpegAudioConverter();

    private static readonly string[] PackagingOnlyFiles = ["info", "author.txt", "title.txt", "note.txt"];

    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryMode = FileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public ThemePackageResult ExportPackage(string themePath, string outputDirectory, string system, string name, string? previewPath, string? baseUiPath, ThemeExportOptions? options = null)
    {
        ValidateArguments(themePath, system, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        options ??= new ThemeExportOptions();

        if (options.ConvertBgmAudio && options.IncludeBgm)
        {
            ConvertBgmFiles(themePath, options, baseUiPath);
        }

        Directory.CreateDirectory(outputDirectory);

        var packagePath = Path.Combine(outputDirectory, $"{system.ToUpperInvariant()}.{name}.tar.gz");
        var temporaryPath = $"{packagePath}.{Guid.NewGuid():N}.tmp";
        var includesPreview = previewPath is not null && File.Exists(previewPath);
        var (files, skipped) = SelectFiles(themePath, baseUiPath, options);
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

    private static (List<string> Files, int Skipped) SelectFiles(string themePath, string? baseUiPath, ThemeExportOptions options)
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

            var isBgm = relativePath.StartsWith("resources/sounds/", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(relativePath).StartsWith("bgm_", StringComparison.OrdinalIgnoreCase);

            if (!options.IncludeBgm && isBgm)
            {
                skipped++;
                continue;
            }

            if (options.ConvertBgmAudio && relativePath.StartsWith("resources/sounds/", StringComparison.OrdinalIgnoreCase))
            {
                var ext = Path.GetExtension(filePath);
                if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(filePath, ".wav")))
                {
                    continue;
                }
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

    private void ConvertBgmFiles(string themePath, ThemeExportOptions options, string? baseUiPath = null)
    {
        if (!options.IncludeBgm)
        {
            return;
        }

        var soundsDir = Path.Combine(themePath, "resources", "sounds");
        if (!Directory.Exists(soundsDir))
        {
            return;
        }

        var audioFiles = Directory.EnumerateFiles(soundsDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (audioFiles.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.FFmpegPath) || !File.Exists(options.FFmpegPath))
        {
            throw new FileNotFoundException("Convert BGM audio is enabled, but the FFmpeg executable path is not configured or does not exist.", options.FFmpegPath);
        }

        foreach (var audioFile in audioFiles)
        {
            if (baseUiPath is not null)
            {
                var relativePath = ToArchivePath(themePath, audioFile);
                var baseFile = Path.Combine(baseUiPath, relativePath);
                if (IsUnchanged(audioFile, baseFile))
                {
                    // Untouched base file: do not convert so delta export can omit it
                    continue;
                }
            }
            var ext = Path.GetExtension(audioFile);
            var isWav = ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
            var targetWavPath = isWav ? audioFile : Path.ChangeExtension(audioFile, ".wav");

            var needsConversion = false;
            if (!isWav)
            {
                needsConversion = true;
            }
            else
            {
                var info = Audio.WavAudioReader.Read(audioFile);
                var expectedChannels = options.BgmChannels == Core.Audio.BgmAudioChannels.Mono ? 1 : 2;
                if (!info.IsValidWav || !info.IsConsoleStandard || info.Channels != expectedChannels)
                {
                    needsConversion = true;
                }
            }

            if (needsConversion)
            {
                var result = _ffmpegAudioConverter.ConvertToConsoleWavAsync(
                    options.FFmpegPath,
                    audioFile,
                    targetWavPath,
                    options.BgmChannels).GetAwaiter().GetResult();

                if (!result.Success)
                {
                    throw new IOException($"Failed to convert '{Path.GetFileName(audioFile)}': {result.ErrorMessage}");
                }
            }
        }
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
