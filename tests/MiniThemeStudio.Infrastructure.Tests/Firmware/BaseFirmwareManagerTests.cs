using System.Formats.Tar;
using System.IO.Compression;
using DiscUtils.SquashFs;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Infrastructure.Firmware;

namespace MiniThemeStudio.Infrastructure.Tests.Firmware;

public sealed class BaseFirmwareManagerTests : IDisposable
{
    private const string UiPath = "usr/share/ui/snes-eur";

    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basesPath;
    private readonly string _uiPath;
    private readonly string _uiTreeSha256;

    public BaseFirmwareManagerTests()
    {
        _basesPath = Path.Combine(_rootPath, "Bases");
        _uiPath = Path.Combine(_rootPath, "rootfs", "usr", "share", "ui", "snes-eur");
        WriteFile(_uiPath, "init.json", """{"engineFormat":9,"luaScripts":["/scripts/env/env_eur.lua"]}""");
        WriteFile(_uiPath, "dependencies.json", "{}");
        WriteFile(_uiPath, "resources/sprites/packed.json", """{"texture":{"id":"a","width":4,"height":4},"sprites":{}}""");
        WriteFile(_uiPath, ".stamp_built", "");
        _uiTreeSha256 = UiTreeHash.ComputeForDirectory(_uiPath).Sha256;
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public async Task ImportsVerifiedTarGzAndRecordsManifest()
    {
        string archivePath = CreateTarGz();
        BaseFirmwareManager manager = CreateManager(CreateDefinition(archivePath, "tar.gz"));

        InstalledBaseInfo result = await manager.ImportFromFileAsync(archivePath);

        Assert.Equal(BaseInstallStatus.Valid, result.Status);
        Assert.Equal(Path.Combine(_basesPath, "snes", "eur", "2.0.13"), result.LocalPath);
        Assert.True(File.Exists(Path.Combine(result.LocalPath, "resources", "sprites", "packed.json")));
        Assert.False(File.Exists(Path.Combine(result.LocalPath, ".stamp_built")));
        Assert.True(File.Exists(Path.Combine(result.LocalPath, BaseFirmwareManager.ManifestFileName)));
        Assert.Equal(BaseInstallStatus.Valid, Assert.Single(manager.GetInstalledBases()).Status);
    }

    [Fact]
    public async Task ImportsVerifiedSquashFsImage()
    {
        string imagePath = CreateSquashFs();
        BaseFirmwareManager manager = CreateManager(CreateDefinition(imagePath, "squashfs"));

        InstalledBaseInfo result = await manager.ImportFromFileAsync(imagePath);

        Assert.Equal(BaseInstallStatus.Valid, result.Status);
        Assert.Equal(_uiTreeSha256, UiTreeHash.ComputeForDirectory(result.LocalPath).Sha256);
    }

    [Fact]
    public async Task RejectsRenamedFileWithUnknownHash()
    {
        string archivePath = CreateTarGz();
        BaseFirmwareManager manager = CreateManager(CreateDefinition(archivePath, "tar.gz"));
        string impostorPath = Path.Combine(_rootPath, "dp-sneseur-release-v2.0.14-0-gd8b65c6.tar.gz");
        File.WriteAllBytes(impostorPath, [0x1f, 0x8b, 1, 2, 3]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportFromFileAsync(impostorPath));

        Assert.Contains("not a recognised original firmware", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_basesPath, "snes")));
    }

    [Fact]
    public async Task RejectsSourceWhoseUiDoesNotMatchAndKeepsExistingBase()
    {
        string archivePath = CreateTarGz();
        FirmwareDefinition definition = CreateDefinition(archivePath, "tar.gz") with { UiTreeSha256 = new string('0', 64) };
        BaseFirmwareManager manager = CreateManager(definition);
        string existingPath = Path.Combine(_basesPath, definition.RelativeStoragePath);
        WriteFile(existingPath, "marker.txt", "preserved");

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportFromFileAsync(archivePath));

        Assert.Equal("preserved", File.ReadAllText(Path.Combine(existingPath, "marker.txt")));
        Assert.Empty(Directory.GetDirectories(_basesPath, ".mts-stage-*"));
    }

    [Fact]
    public async Task ImportsFolderOnlyWhenContentMatchesOriginal()
    {
        BaseFirmwareManager manager = CreateManager(CreateDefinition(CreateTarGz(), "tar.gz"));
        string modifiedPath = Path.Combine(_rootPath, "modified");
        CopyDirectory(_uiPath, modifiedPath);
        WriteFile(modifiedPath, "resources/sprites/packed.json", "{}");

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportFromDirectoryAsync(modifiedPath));
        InstalledBaseInfo result = await manager.ImportFromDirectoryAsync(_uiPath);

        Assert.Equal(BaseInstallStatus.Valid, result.Status);
    }

    [Fact]
    public async Task SecondImportReusesInstalledBase()
    {
        string archivePath = CreateTarGz();
        BaseFirmwareManager manager = CreateManager(CreateDefinition(archivePath, "tar.gz"));
        InstalledBaseInfo first = await manager.ImportFromFileAsync(archivePath);
        string manifestPath = Path.Combine(first.LocalPath, BaseFirmwareManager.ManifestFileName);
        string manifest = File.ReadAllText(manifestPath);

        InstalledBaseInfo second = await manager.ImportFromDirectoryAsync(_uiPath);

        Assert.Equal(first.LocalPath, second.LocalPath);
        Assert.Equal(manifest, File.ReadAllText(manifestPath));
    }

    [Fact]
    public void FlagsBaseFolderWithoutManifestAsInvalid()
    {
        FirmwareDefinition definition = CreateDefinition(CreateTarGz(), "tar.gz");
        BaseFirmwareManager manager = CreateManager(definition);
        CopyDirectory(_uiPath, Path.Combine(_basesPath, definition.RelativeStoragePath));

        InstalledBaseInfo info = Assert.Single(manager.GetInstalledBases());

        Assert.Equal(BaseInstallStatus.Invalid, info.Status);
        Assert.Null(manager.FindCompatibleBaseUiPath());
    }

    [Fact]
    public async Task ConsoleImportPrefersOriginalSquashFsPath()
    {
        var client = new FakeConsoleClient(new Dictionary<string, string>
        {
            ["/var/squashfs/usr/share/ui/snes-eur"] = _uiPath,
            ["/usr/share/ui/snes-eur"] = Path.Combine(_rootPath, "missing"),
        });
        BaseFirmwareManager manager = CreateManager(CreateDefinition(CreateTarGz(), "tar.gz"), client);

        InstalledBaseInfo result = await manager.ImportFromConsoleAsync(new ConsoleConnectionOptions("169.254.13.37"));

        Assert.Equal(BaseInstallStatus.Valid, result.Status);
        Assert.Equal(["/var/squashfs/usr/share/ui/snes-eur"], client.Downloads);
    }

    [Fact]
    public async Task ConsoleImportRejectsModifiedUi()
    {
        string modifiedPath = Path.Combine(_rootPath, "modified");
        CopyDirectory(_uiPath, modifiedPath);
        WriteFile(modifiedPath, "resources/sprites/packed.png", "theme");
        var client = new FakeConsoleClient(new Dictionary<string, string> { ["/usr/share/ui/snes-eur"] = modifiedPath });
        BaseFirmwareManager manager = CreateManager(CreateDefinition(CreateTarGz(), "tar.gz"), client);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.ImportFromConsoleAsync(new ConsoleConnectionOptions("169.254.13.37")));

        Assert.Contains("modified", exception.Message);
    }

    [Fact]
    public async Task FindsBaseMatchingThemeRegion()
    {
        BaseFirmwareManager manager = CreateManager(CreateDefinition(CreateTarGz(), "tar.gz"));
        await manager.ImportFromDirectoryAsync(_uiPath);
        string themePath = Path.Combine(_rootPath, "theme");
        WriteFile(themePath, "init.json", """{"engineFormat":9,"luaScripts":["/scripts/env/env_eur.lua"]}""");
        string nesThemePath = Path.Combine(_rootPath, "other");
        WriteFile(nesThemePath, "init.json", """{"engineFormat":8,"luaScripts":["/scripts/colors_nes.lua"]}""");

        Assert.Equal(Path.Combine(_basesPath, "snes", "eur", "2.0.13"), manager.FindCompatibleBaseUiPath(themePath));
        Assert.Null(manager.FindCompatibleBaseUiPath(nesThemePath));
    }

    private BaseFirmwareManager CreateManager(FirmwareDefinition definition, IConsoleDirectoryClient? client = null)
    {
        return new BaseFirmwareManager(
            new FirmwareRegistry([definition]),
            client is null ? null : new FakeClientFactory(client),
            _basesPath);
    }

    private FirmwareDefinition CreateDefinition(string sourcePath, string format)
    {
        return FirmwareRegistryTests.CreateDefinition(
            "snes-eur-2.0.13",
            _uiTreeSha256,
            UiTreeHash.ComputeFileSha256(sourcePath),
            format,
            UiPath);
    }

    private string CreateTarGz()
    {
        string archivePath = Path.Combine(_rootPath, "firmware.tar.gz");
        if (!File.Exists(archivePath))
        {
            using FileStream output = File.Create(archivePath);
            using var gzip = new GZipStream(output, CompressionLevel.Fastest);
            TarFile.CreateFromDirectory(Path.Combine(_rootPath, "rootfs"), gzip, includeBaseDirectory: false);
        }

        return archivePath;
    }

    private string CreateSquashFs()
    {
        string imagePath = Path.Combine(_rootPath, "firmware.hsqs");
        var builder = new SquashFileSystemBuilder();
        string rootfsPath = Path.Combine(_rootPath, "rootfs");
        foreach (string filePath in Directory.EnumerateFiles(rootfsPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(rootfsPath, filePath).Replace('/', '\\');
            builder.AddFile(relativePath, new MemoryStream(File.ReadAllBytes(filePath)));
        }

        using FileStream output = File.Create(imagePath);
        builder.Build(output);
        return imagePath;
    }

    private static void WriteFile(string rootPath, string relativePath, string content)
    {
        string path = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string targetPath = Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, filePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(filePath, targetPath);
        }
    }

    private sealed class FakeClientFactory(IConsoleDirectoryClient client) : IConsoleDirectoryClientFactory
    {
        public IConsoleDirectoryClient Create(ConsoleConnectionOptions options) => client;
    }

    private sealed class FakeConsoleClient(IReadOnlyDictionary<string, string> remoteDirectories) : IConsoleDirectoryClient
    {
        public List<string> Downloads { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(remoteDirectories.TryGetValue(remotePath, out string? localPath) && Directory.Exists(localPath));
        }

        public Task DownloadDirectoryAsync(string remotePath, string localPath, CancellationToken cancellationToken)
        {
            Downloads.Add(remotePath);
            CopyDirectory(remoteDirectories[remotePath], localPath);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
