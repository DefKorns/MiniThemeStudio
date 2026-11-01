using System.Text.Json;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.Infrastructure.Firmware;

public sealed class BaseFirmwareManager : IBaseFirmwareManager
{
    public const string ManifestFileName = ".mts-base.json";

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IFirmwareRegistry _registry;
    private readonly IConsoleDirectoryClientFactory? _consoleClientFactory;

    public BaseFirmwareManager(
        IFirmwareRegistry registry,
        IConsoleDirectoryClientFactory? consoleClientFactory = null,
        string? basesRootPath = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _consoleClientFactory = consoleClientFactory;
        BasesRootPath = string.IsNullOrWhiteSpace(basesRootPath) ? DefaultBasesRootPath : basesRootPath;
    }

    public static string DefaultBasesRootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MiniThemeStudio",
        "Bases");

    public string BasesRootPath { get; }

    public IReadOnlyList<InstalledBaseInfo> GetInstalledBases()
    {
        return _registry.All.Select(GetInstalledBase).ToList();
    }

    public string? FindCompatibleBaseUiPath(string? themePath = null)
    {
        var installed = GetInstalledBases()
            .Where(info => info.Status == BaseInstallStatus.Valid)
            .OrderByDescending(info => Version.TryParse(info.Definition.Version, out Version? version) ? version : new Version())
            .ToList();
        if (installed.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(themePath))
        {
            return installed[0].LocalPath;
        }

        (string? console, string? region) = DetectThemeTarget(themePath);
        if (console is null)
        {
            return null;
        }

        var sameConsole = installed.Where(info => info.Definition.Console == console).ToList();
        return (sameConsole.FirstOrDefault(info => info.Definition.Region == region) ?? sameConsole.FirstOrDefault())?.LocalPath;
    }

    public async Task<InstalledBaseInfo> ImportFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Firmware file not found.", filePath);
        }

        string fileName = Path.GetFileName(filePath);
        string sha256 = await Task.Run(() => UiTreeHash.ComputeFileSha256(filePath), cancellationToken);
        (FirmwareDefinition Definition, FirmwareSource Source) match = _registry.FindBySourceSha256(sha256)
            ?? throw new InvalidDataException(
                $"'{fileName}' is not a recognised original firmware file (SHA-256 {sha256}). "
                + "Only unmodified official images listed in the firmware registry are accepted.");
        (FirmwareDefinition? definition, FirmwareSource? source) = match;

        InstalledBaseInfo existing = GetInstalledBase(definition);
        if (existing.Status == BaseInstallStatus.Valid)
        {
            return existing;
        }

        FirmwareSourceFormat format = FirmwareSourceReader.Detect(filePath);
        if (FirmwareSourceReader.FormatName(format) != source.Format)
        {
            throw new InvalidDataException($"'{fileName}' should be a {source.Format} file but its content is {FirmwareSourceReader.FormatName(format)}.");
        }

        string stagingPath = CreateStagingPath();
        try
        {
            await Task.Run(
                () => FirmwareSourceReader.ExtractDirectory(filePath, format, definition.UiPath, stagingPath, cancellationToken),
                cancellationToken);
            return await VerifyAndPublishAsync(stagingPath, definition, $"file:{fileName}", sha256, cancellationToken);
        }
        finally
        {
            DeleteDirectory(stagingPath);
        }
    }

    public async Task<InstalledBaseInfo> ImportFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {directoryPath}");
        }

        UiTreeFingerprint fingerprint = await Task.Run(() => UiTreeHash.ComputeForDirectory(directoryPath, cancellationToken), cancellationToken);
        FirmwareDefinition definition = _registry.FindByUiTreeSha256(fingerprint.Sha256)
            ?? throw new InvalidDataException(
                "This folder does not match any original Base UI. Select the UI folder itself "
                + "(the one containing init.json) from an unmodified firmware.");

        InstalledBaseInfo existing = GetInstalledBase(definition);
        if (existing.Status == BaseInstallStatus.Valid)
        {
            return existing;
        }

        string stagingPath = CreateStagingPath();
        try
        {
            await Task.Run(() => CopyDirectory(directoryPath, stagingPath, cancellationToken), cancellationToken);
            return await VerifyAndPublishAsync(stagingPath, definition, $"folder:{directoryPath}", null, cancellationToken);
        }
        finally
        {
            DeleteDirectory(stagingPath);
        }
    }

    public async Task<InstalledBaseInfo> ImportFromConsoleAsync(ConsoleConnectionOptions connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (_consoleClientFactory is null)
        {
            throw new InvalidOperationException("Console import is not available.");
        }

        await using IConsoleDirectoryClient client = _consoleClientFactory.Create(connection);
        await client.ConnectAsync(cancellationToken);

        foreach (string? uiPath in _registry.All.Select(definition => definition.UiPath).Distinct())
        {
            FirmwareDefinition definition = _registry.All.First(candidate => candidate.UiPath == uiPath);
            string? remotePath = await client.DirectoryExistsAsync(definition.OriginalConsoleUiPath, cancellationToken)
                ? definition.OriginalConsoleUiPath
                : await client.DirectoryExistsAsync(definition.ConsoleUiPath, cancellationToken)
                    ? definition.ConsoleUiPath
                    : null;
            if (remotePath is null)
            {
                continue;
            }

            string stagingPath = CreateStagingPath();
            try
            {
                await client.DownloadDirectoryAsync(remotePath, stagingPath, cancellationToken);
                UiTreeFingerprint fingerprint = await Task.Run(() => UiTreeHash.ComputeForDirectory(stagingPath, cancellationToken), cancellationToken);
                FirmwareDefinition? match = _registry.FindByUiTreeSha256(fingerprint.Sha256);
                if (match is null || match.UiPath != uiPath)
                {
                    throw new InvalidDataException(
                        $"The Base UI at {remotePath} on the console does not match any original firmware. "
                        + "It may be modified by an installed theme or mod.");
                }

                InstalledBaseInfo existing = GetInstalledBase(match);
                if (existing.Status == BaseInstallStatus.Valid)
                {
                    return existing;
                }

                return await VerifyAndPublishAsync(stagingPath, match, $"console:{connection.Host}{remotePath}", null, cancellationToken);
            }
            finally
            {
                DeleteDirectory(stagingPath);
            }
        }

        throw new DirectoryNotFoundException("No supported Base UI was found on the connected console.");
    }

    private InstalledBaseInfo GetInstalledBase(FirmwareDefinition definition)
    {
        string localPath = Path.Combine(BasesRootPath, definition.RelativeStoragePath);
        if (!Directory.Exists(localPath))
        {
            return new InstalledBaseInfo(definition, localPath, BaseInstallStatus.Missing);
        }

        BaseManifest? manifest = ReadManifest(localPath);
        if (manifest is null)
        {
            return new InstalledBaseInfo(definition, localPath, BaseInstallStatus.Invalid, "Not verified: import manifest missing.");
        }

        return string.Equals(manifest.UiTreeSha256, definition.UiTreeSha256, StringComparison.OrdinalIgnoreCase)
            ? new InstalledBaseInfo(definition, localPath, BaseInstallStatus.Valid)
            : new InstalledBaseInfo(definition, localPath, BaseInstallStatus.Invalid, "Content does not match the registry.");
    }

    private async Task<InstalledBaseInfo> VerifyAndPublishAsync(
        string stagingPath,
        FirmwareDefinition definition,
        string origin,
        string? sourceSha256,
        CancellationToken cancellationToken)
    {
        UiTreeFingerprint fingerprint = await Task.Run(() => UiTreeHash.ComputeForDirectory(stagingPath, cancellationToken), cancellationToken);
        if (!string.Equals(fingerprint.Sha256, definition.UiTreeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The extracted Base UI does not match {definition.DisplayName} v{definition.Version} "
                + $"({fingerprint.FileCount} files, expected {definition.UiFileCount}).");
        }

        var manifest = new BaseManifest(definition.Id, definition.UiTreeSha256, fingerprint.FileCount, origin, sourceSha256, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            Path.Combine(stagingPath, ManifestFileName),
            JsonSerializer.Serialize(manifest, ManifestSerializerOptions),
            cancellationToken);

        string destinationPath = Path.Combine(BasesRootPath, definition.RelativeStoragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string backupPath = $"{destinationPath}.mts-backup-{Guid.NewGuid():N}";
        bool destinationMoved = false;

        try
        {
            if (Directory.Exists(destinationPath))
            {
                Directory.Move(destinationPath, backupPath);
                destinationMoved = true;
            }

            Directory.Move(stagingPath, destinationPath);
            DeleteDirectory(backupPath);
            return new InstalledBaseInfo(definition, destinationPath, BaseInstallStatus.Valid);
        }
        catch
        {
            if (destinationMoved && !Directory.Exists(destinationPath))
            {
                Directory.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static (string? Console, string? Region) DetectThemeTarget(string themePath)
    {
        string initPath = Path.Combine(themePath, "init.json");
        if (File.Exists(initPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(initPath));
                JsonElement root = document.RootElement;
                int engineFormat = root.TryGetProperty("engineFormat", out JsonElement format) ? format.GetInt32() : 0;
                string[] scripts = root.TryGetProperty("luaScripts", out JsonElement scriptList)
                    ? scriptList.EnumerateArray().Select(script => script.GetString() ?? "").ToArray()
                    : [];

                if (engineFormat >= 10)
                {
                    return ("shonen", "jpn");
                }

                foreach ((string? script, string? region) in new[] { ("/env_eur.lua", "eur"), ("/env_usa.lua", "usa"), ("/env_jpn.lua", "jpn") })
                {
                    if (scripts.Any(candidate => candidate.EndsWith(script, StringComparison.Ordinal)))
                    {
                        return ("snes", region);
                    }
                }

                if (scripts.Any(candidate => candidate.EndsWith("/colors_nes.lua", StringComparison.Ordinal)))
                {
                    return ("nes", "usa-eur");
                }

                if (scripts.Any(candidate => candidate.EndsWith("/colors_hvc.lua", StringComparison.Ordinal)))
                {
                    return ("famicom", "jpn");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
            {
            }
        }

        string[] segments = themePath.Replace('\\', '/').ToLowerInvariant().Split('/');
        if (segments.Any(segment => segment.Contains("snes")))
        {
            return ("snes", "eur");
        }

        if (segments.Any(segment => segment.Contains("nes")))
        {
            return ("nes", "usa-eur");
        }

        return (null, null);
    }

    private static BaseManifest? ReadManifest(string basePath)
    {
        string manifestPath = Path.Combine(basePath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BaseManifest>(File.ReadAllText(manifestPath), ManifestSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string CreateStagingPath()
    {
        Directory.CreateDirectory(BasesRootPath);
        return Path.Combine(BasesRootPath, $".mts-stage-{Guid.NewGuid():N}");
    }

    private static void CopyDirectory(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourcePath, filePath);
            if (UiTreeHash.IsIgnored(relativePath))
            {
                continue;
            }

            string targetPath = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(filePath, targetPath);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private sealed record BaseManifest(
        string Id,
        string UiTreeSha256,
        int FileCount,
        string Origin,
        string? SourceSha256,
        DateTimeOffset ImportedAt);
}
