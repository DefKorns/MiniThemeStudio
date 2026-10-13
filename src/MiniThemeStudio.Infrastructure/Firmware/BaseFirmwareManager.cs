using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Firmware;

public sealed class BaseFirmwareManager : IBaseFirmwareManager
{
    private readonly IFirmwareRegistry _registry;
    private readonly IBaseUiValidator _validator;

    public string BasesRootPath { get; }

    public BaseFirmwareManager(
        IFirmwareRegistry registry,
        IBaseUiValidator? validator = null,
        string? basesRootPath = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? new BaseUiValidator();

        if (!string.IsNullOrWhiteSpace(basesRootPath))
        {
            BasesRootPath = basesRootPath;
        }
        else
        {
            // Primary path: %AppData%/MiniThemeManager/Bases/
            var primary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MiniThemeManager",
                "Bases");

            // Fallback path: %AppData%/MiniThemeStudio/base-ui
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MiniThemeStudio",
                "base-ui");

            BasesRootPath = Directory.Exists(fallback) && !Directory.Exists(primary)
                ? fallback
                : primary;
        }
    }

    public IReadOnlyList<InstalledBaseInfo> GetInstalledBases()
    {
        var result = new List<InstalledBaseInfo>();
        foreach (var def in _registry.All)
        {
            var targetPath = Path.Combine(BasesRootPath, def.RelativeStoragePath);
            var legacyPath = Path.Combine(BasesRootPath, def.LegacyFolderName);

            var resolvedPath = Directory.Exists(targetPath)
                ? targetPath
                : Directory.Exists(legacyPath)
                    ? legacyPath
                    : targetPath;

            if (Directory.Exists(resolvedPath))
            {
                var missing = GetMissingBaseFiles(resolvedPath);
                result.Add(new InstalledBaseInfo(
                    def,
                    resolvedPath,
                    missing.Count == 0 ? BaseInstallStatus.Valid : BaseInstallStatus.Incomplete,
                    missing));
            }
            else
            {
                result.Add(new InstalledBaseInfo(
                    def,
                    targetPath,
                    BaseInstallStatus.Missing,
                    ["Directory not found"]));
            }
        }

        return result;
    }

    public InstalledBaseInfo? GetInstalledBase(string id)
    {
        return GetInstalledBases().FirstOrDefault(b =>
            string.Equals(b.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public string? FindCompatibleBaseUiPath(string? hint = null)
    {
        var installed = GetInstalledBases().Where(b => b.Status == BaseInstallStatus.Valid).ToList();
        if (installed.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(hint))
        {
            return installed[0].LocalPath;
        }

        var lowerHint = hint.ToLowerInvariant().Replace('\\', '/');

        // Look for exact matches in console or region
        var matched = installed.FirstOrDefault(b =>
            lowerHint.Contains(b.Definition.Console, StringComparison.OrdinalIgnoreCase) &&
            lowerHint.Contains(b.Definition.Region, StringComparison.OrdinalIgnoreCase));

        if (matched != null)
        {
            return matched.LocalPath;
        }

        // Check console match
        matched = installed.FirstOrDefault(b =>
            lowerHint.Contains(b.Definition.Console, StringComparison.OrdinalIgnoreCase));

        if (matched != null)
        {
            return matched.LocalPath;
        }

        // Check region match
        matched = installed.FirstOrDefault(b =>
            lowerHint.Contains(b.Definition.Region, StringComparison.OrdinalIgnoreCase));

        return matched?.LocalPath ?? installed[0].LocalPath;
    }

    public async Task<InstalledBaseInfo> ImportFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {directoryPath}");
        }

        _validator.Validate(directoryPath);

        var definition = _registry.MatchExtractedFolder(directoryPath)
            ?? new FirmwareDefinition(
                $"custom-{new DirectoryInfo(directoryPath).Name.ToLowerInvariant()}",
                "custom",
                "all",
                "1.0.0",
                new DirectoryInfo(directoryPath).Name,
                "*",
                "",
                "",
                "");

        var destination = Path.Combine(BasesRootPath, definition.RelativeStoragePath);

        // Avoid duplicate copy if already valid
        if (Directory.Exists(destination) && GetMissingBaseFiles(destination).Count == 0)
        {
            return new InstalledBaseInfo(definition, destination, BaseInstallStatus.Valid, []);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = Path.Combine(BasesRootPath, $".tmp-stage-{Guid.NewGuid():N}");
        try
        {
            await Task.Run(() => CopyDirectory(new DirectoryInfo(directoryPath), Directory.CreateDirectory(staging)), cancellationToken);
            _validator.Validate(staging);

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.Move(staging, destination);
            return new InstalledBaseInfo(definition, destination, BaseInstallStatus.Valid, []);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    public async Task<InstalledBaseInfo> ImportFromArchiveAsync(
        string archiveFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFilePath);
        if (!File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException($"Archive not found: {archiveFilePath}");
        }

        if (IsDiscImage(archiveFilePath, out var discReason))
        {
            throw new InvalidDataException(discReason);
        }

        var matchedDefinition = _registry.MatchArchive(archiveFilePath);
        var staging = Path.Combine(BasesRootPath, $".tmp-tar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        try
        {
            await Task.Run(() => ExtractUiFromTar(archiveFilePath, staging), cancellationToken);

            var extractedUiRoot = FindUiRootInDirectory(staging);
            if (extractedUiRoot == null)
            {
                throw new InvalidDataException("The archive does not contain a valid Base UI structure (missing resources/, init.json, dependencies.json).");
            }

            _validator.Validate(extractedUiRoot);

            var finalDefinition = matchedDefinition ?? _registry.MatchExtractedFolder(extractedUiRoot)
                ?? new FirmwareDefinition(
                    Path.GetFileNameWithoutExtension(archiveFilePath).ToLowerInvariant(),
                    "unknown",
                    "all",
                    "1.0.0",
                    Path.GetFileName(archiveFilePath),
                    Path.GetFileName(archiveFilePath),
                    "",
                    "",
                    "");

            var destination = Path.Combine(BasesRootPath, finalDefinition.RelativeStoragePath);
            if (Directory.Exists(destination) && GetMissingBaseFiles(destination).Count == 0)
            {
                return new InstalledBaseInfo(finalDefinition, destination, BaseInstallStatus.Valid, []);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.Move(extractedUiRoot, destination);
            return new InstalledBaseInfo(finalDefinition, destination, BaseInstallStatus.Valid, []);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch { /* best effort */ }
            }
        }
    }

    public bool IsDiscImage(string filePath, out string? reason)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".hsqs" || ext == ".sfs")
        {
            reason = "SquashFS image detected (.hsqs). Nintendo stock packages include a companion .tar.gz archive (e.g. dp-*-release-*.tar.gz) with the full uncompressed filesystem. Please select the .tar.gz or .tar file instead.";
            return true;
        }

        if (ext == ".img")
        {
            reason = "Kernel/disk image detected (.img). MiniThemeManager extracts Base UI from rootfs archives (.tar.gz / .tar) or directly from console.";
            return true;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[4];
            if (stream.Read(header, 0, 4) == 4)
            {
                // "sqsh" (0x73, 0x71, 0x73, 0x68) or "hsqs" (0x68, 0x73, 0x71, 0x73)
                if ((header[0] == 0x73 && header[1] == 0x71 && header[2] == 0x73 && header[3] == 0x68) ||
                    (header[0] == 0x68 && header[1] == 0x73 && header[2] == 0x71 && header[3] == 0x73))
                {
                    reason = "SquashFS disk image detected. Please provide the companion .tar.gz archive or extract it first.";
                    return true;
                }
            }
        }
        catch
        {
            // Ignore file read error here
        }

        reason = null;
        return false;
    }

    private static void ExtractUiFromTar(string archiveFilePath, string targetDirectory)
    {
        using var fileStream = File.OpenRead(archiveFilePath);
        Stream tarStream = fileStream;

        var isGzip = archiveFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                     archiveFilePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

        GZipStream? gzipStream = null;
        if (isGzip)
        {
            gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            tarStream = gzipStream;
        }

        try
        {
            using var tarReader = new TarReader(tarStream);
            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) != null)
            {
                var entryName = entry.Name.Replace('\\', '/');

                // Filter only UI relevant paths or extract if under share/ui or clover-ui
                var isUiCandidate = entryName.Contains("usr/share/ui", StringComparison.OrdinalIgnoreCase) ||
                                    entryName.Contains("usr/share/clover-ui", StringComparison.OrdinalIgnoreCase) ||
                                    entryName.Contains("clover-ui", StringComparison.OrdinalIgnoreCase) ||
                                    entryName.Contains("resources", StringComparison.OrdinalIgnoreCase) ||
                                    entryName.EndsWith("init.json", StringComparison.OrdinalIgnoreCase) ||
                                    entryName.EndsWith("dependencies.json", StringComparison.OrdinalIgnoreCase);

                if (!isUiCandidate && !entry.EntryType.HasFlag(TarEntryType.Directory))
                {
                    continue;
                }

                // Protect against path traversal
                var cleanEntryPath = entryName.TrimStart('/', '.');
                var destinationPath = Path.Combine(targetDirectory, cleanEntryPath);
                var fullDest = Path.GetFullPath(destinationPath);
                var fullTarget = Path.GetFullPath(targetDirectory);

                if (!fullDest.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }
        }
        finally
        {
            gzipStream?.Dispose();
        }
    }

    private static string? FindUiRootInDirectory(string directoryPath)
    {
        // 1. Direct check
        if (IsValidBaseUiStructure(directoryPath))
        {
            return directoryPath;
        }

        // 2. Search subdirectories
        foreach (var sub in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            if (IsValidBaseUiStructure(sub))
            {
                return sub;
            }
        }

        return null;
    }

    private static bool IsValidBaseUiStructure(string path)
    {
        return Directory.Exists(Path.Combine(path, "resources")) &&
               File.Exists(Path.Combine(path, "dependencies.json")) &&
               File.Exists(Path.Combine(path, "init.json"));
    }

    private static List<string> GetMissingBaseFiles(string path)
    {
        var missing = new List<string>();
        if (!Directory.Exists(Path.Combine(path, "resources")))
        {
            missing.Add("resources/");
        }
        if (!File.Exists(Path.Combine(path, "dependencies.json")))
        {
            missing.Add("dependencies.json");
        }
        if (!File.Exists(Path.Combine(path, "init.json")))
        {
            missing.Add("init.json");
        }
        return missing;
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
    {
        foreach (var dir in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source.FullName, dir.FullName);
            Directory.CreateDirectory(Path.Combine(destination.FullName, rel));
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source.FullName, file.FullName);
            var destPath = Path.Combine(destination.FullName, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            file.CopyTo(destPath, true);
        }
    }
}
